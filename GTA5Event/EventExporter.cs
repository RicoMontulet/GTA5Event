using GTA;
using GTA.Native;
using IniParser;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;


namespace GTA5Event
{
    class EventExporter : Script
    {
        private readonly string dataPath;
        private readonly string logFilePath;
        private readonly Weather[] wantedWeather;
        private readonly string[] Actions;
        private readonly int[] Fps_Options;
        private readonly TimeSpan[] TimesOfDay;
        private readonly int LowerGroupDiv;
        private readonly int LowerGroupSizeDiv;
        private readonly int MinGroupSize;
        private readonly int MaxPeds;
        private readonly Player player;
        private bool notificationsEnabled = true;
        private static readonly Random random = new Random();
        private readonly List<Ped> spawnedPeds = new List<Ped>();
        private readonly List<Vehicle> spawnedVehicles = new List<Vehicle>();

        private int lastImageId = -1;
        private readonly Size screenResolution = GTA.UI.Screen.Resolution;

        private readonly List<GTAframe> buffer = new List<GTAframe>();

        private readonly List<GTALocation> locations = new List<GTALocation>();
        private readonly SortedSet<string> processed = new SortedSet<string>();
        private int currentLocation = -1;
        private int frameCounter = 0;
        private string rootDir;
        private string processedDir;
        private readonly int TotalFrames;
        private readonly int EventFrame;
        private string locationDir;
        private int LocationCounter = 0;
        private int frameRenderDelay;
        private int sleepTimeRemainder = 1;
        private ModState currentState = ModState.INIT;
        private GTA.Math.Vector3 TempRoiPoint = new GTA.Math.Vector3(0, 0, 0);
        private RaycastResult TempRayCastResult = new RaycastResult();
        private static bool processing = false;
        private static byte[] depth;
        private static byte[] stencil;
        private static Bitmap color;

        public EventExporter()
        {
            GTA.UI.Screen.ShowHelpTextThisFrame(Directory.GetCurrentDirectory() + "/Scripts/gta_config.ini");
            var parser = new FileIniDataParser();
            var data = parser.ReadFile(Directory.GetCurrentDirectory() + "/Scripts/gta_config.ini");

            dataPath = data["directories"]["location_dir"];

            logFilePath = data["directories"]["log_dir"];

            // Parse config
            var config = data["config"];
            LowerGroupDiv = int.Parse(config["lowerGroupDiv"]);
            LowerGroupSizeDiv = int.Parse(config["lowerGroupSizeDiv"]);
            MinGroupSize = int.Parse(config["minGroupSize"]);
            MaxPeds = int.Parse(config["maxPeds"]);


            TotalFrames = int.Parse(config["n_frames"]);
            EventFrame = int.Parse(config["event_frame"]);
            if (EventFrame > TotalFrames)
            {
                GTA.UI.Notification.Show("Warning! EventFrame > TotalFrames so event will never happen!");
            }
            if (EventFrame < 0)
            {
                GTA.UI.Notification.Show("Warning! EventFrame < 0 event will happen immediatly!");

            }
            if (TotalFrames < 1)
            {
                GTA.UI.Notification.Show("Warning! Totalframes < 1. Script will crash now");
                throw new Exception("Totalframes < 1");

            }
            notificationsEnabled = bool.Parse(config["in_game_notifications"]);

            var weathers = new List<Weather>();
            foreach (var w in config["weather"].Replace(" ", "").Split(','))
            {
                weathers.Add((Weather)Enum.Parse(typeof(Weather), w));
            }
            wantedWeather = weathers.ToArray();

            Actions = config["actions"].Replace(" ", "").Split(',');

            var fpss = new List<int>();
            foreach (var w in config["fps"].Replace(" ", "").Split(','))
            {
                fpss.Add(int.Parse(w));
            }
            Fps_Options = fpss.ToArray();

            var start_time = config["time_start"].Replace(" ", "").Split(':');
            var end_time = config["time_end"].Replace(" ", "").Split(':');
            int time_intervals = int.Parse(config["time_intervals"].Replace(" ", ""));

            var start_timespan = new TimeSpan(int.Parse(start_time[0]), int.Parse(start_time[1]), int.Parse(start_time[2]));
            var end_timespan = new TimeSpan(int.Parse(end_time[0]), int.Parse(end_time[1]), int.Parse(end_time[2]));

            var time_delta = TimeSpan.FromTicks((end_timespan - start_timespan).Ticks / (time_intervals - 1));

            var time_stamps = new List<TimeSpan>();
            for (int i = 0; i < time_intervals; i++)
            {
                time_stamps.Add(start_timespan);
                start_timespan += time_delta;
            }
            TimesOfDay = time_stamps.ToArray();

            File.WriteAllText(logFilePath, "EventExporter constructor called.\n");

            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            player = Game.Player;
            //this.Tick += new EventHandler(this.OnTick);
            this.KeyDown += this.OnKeyDown;
            //Interval = 300;
            currentLocation = -1;

            GTA.UI.Notification.Show(
                "settings:\n" +
                "location_dir:" + dataPath + "\n" +
                "log_dir:" + logFilePath + "\n" +
                "weathers:" + string.Join(",", wantedWeather) + "\n" +
                "actions:" + string.Join(",", Actions) + "\n" +
                "fps:" + string.Join(",", Fps_Options) + "\n" +
                "times_of_day:" + string.Join(",", TimesOfDay) + "\n"
            );
            //Wait(10000);
        }

        private void WriteFiles(GTAData data, Bitmap color, byte[] depth, byte[] stencil, string path, bool forceWrite = false)
        {
            buffer.Add(new GTAframe
            {
                Color = color,
                Depth = depth,
                Stencil = stencil,
                Path = path,
                Data = data
            });
            if (buffer.Count > 25 || forceWrite) // Buffer some (25) images before spawning a new thread to write the results to disk
            {
                GTAframe[] gtaFrames = buffer.ToArray(); // Clone the buffer into array
                buffer.Clear(); // Clear the buffer

                new Thread(() =>
                {
                    for (int i = 0; i < gtaFrames.Length; i++)
                    {
                        try
                        {
                            GTAframe f = gtaFrames[i];
                            string jsonName = f.Path + ".json";
                            ImageUtils.WriteToTiff(f.Path, screenResolution.Width, screenResolution.Height, f.Color, f.Depth, f.Stencil); // Write the color bitmap, depth and stencil info to disk
                            File.Create(jsonName).Close();
                            using (StreamWriter file = File.AppendText(jsonName))
                            {
                                file.Write(JsonConvert.SerializeObject(f.Data)); // Write the annotations to a json
                            }
                        }
                        catch (Exception exception)
                        {
                            File.AppendAllText(logFilePath, "ERROR2: " + exception.ToString() + "\n");
                            File.AppendAllText(logFilePath, "STACK2: " + exception.StackTrace.ToString() + "\n");
                        }
                        gtaFrames[i] = null; // Free up memory while writing ?
                    }
                    gtaFrames = null; // Free the array ?
                }).Start();
            }
        }

        public class GTAframe // datastructure to hold all information of a single frame
        {
            public Bitmap Color { get; set; }
            public byte[] Stencil { get; set; }
            public byte[] Depth { get; set; }
            public GTAData Data { get; set; }
            public string Path { get; set; }
        }

        public void MoveToNextLocation() // Go to the next location and skip all locations that have been created already
        {
            currentLocation++;
            if (currentLocation < locations.Count)
            {
                GTALocation tempLocation = locations[currentLocation];
                // skip already processed locations
                if (processed.Contains(tempLocation.LocationName))
                {
                    MoveToNextLocation();
                    return;
                }

                locationDir = Path.Combine(rootDir, tempLocation.LocationName);

                // Put the camera in the new position
                player.Character.Position = new GTA.Math.Vector3(tempLocation.PlayerPosition.X, tempLocation.PlayerPosition.Y, tempLocation.PlayerPosition.Z);
                player.Character.Rotation = new GTA.Math.Vector3(tempLocation.PlayerRotation.X, tempLocation.PlayerRotation.Y, tempLocation.PlayerRotation.Z);
                World.RenderingCamera.Position = new GTA.Math.Vector3(tempLocation.CameraPosition.X, tempLocation.CameraPosition.Y, tempLocation.CameraPosition.Z);
                World.RenderingCamera.Rotation = new GTA.Math.Vector3(tempLocation.CameraRotation.X, tempLocation.CameraRotation.Y, tempLocation.CameraRotation.Z);

                Directory.CreateDirectory(locationDir);
                //if (Directory.Exists(locationDir)) // skip already processed locations
                //{
                //    MoveToNextLocation();
                //}
                //else
                //{
                //    Directory.CreateDirectory(locationDir);
                //}
            }
        }

        public void OnTick(object o, EventArgs e)
        {
            // Safety if this takes longer than Interval it might be called again
            if (processing)
            {
                return;
            }
            processing = true;

            try
            {
                // This function won't do anything in this state
                if (currentState == ModState.CHOOSE_LOCATION)
                {
                    return;
                }
                // This function won't do anything in this state
                else if (currentState == ModState.DONE)
                {
                    return;
                }
                // This is only to try random stuff in (probably not in final version)
                else if (currentState == ModState.MESS_AROUND)
                {
                    GTA.Math.Vector3 new_pos = World.RenderingCamera.Position + World.RenderingCamera.Direction * 0.5f;
                    //float x = Game.GetControlNormal(0, GTA.Control.CursorX);
                    //float y = Game.GetControlNormal(0, GTA.Control.CursorY);
                    TempRoiPoint = Utils.RaycastFromCoord(0, 0, player.Character, 500f, 1f, out TempRayCastResult);
                    HashFunctions.Draw3DLine(new_pos, TempRoiPoint);
                    World.DrawMarker(MarkerType.DebugSphere, TempRoiPoint, GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, new GTA.Math.Vector3(0.2f, 0.2f, 0.2f), System.Drawing.Color.Red);
                    GTAData.ShowBoundingBoxes();
                    return;
                }
                // This function draws current ROI if its not empty and projects where the next point will be placed at the end of the cursor and saves it in TempRoiPoint
                else if (currentState == ModState.CHOOSE_ROI)
                {
                    DrawCurrentROI(locations[currentLocation].ROI);
                    TempRoiPoint = Utils.RaycastFromCoord(0f, 0f, player.Character, 500f, 1f);
                    World.DrawMarker(MarkerType.DebugSphere, TempRoiPoint, GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, new GTA.Math.Vector3(0.2f, 0.2f, 0.2f), System.Drawing.Color.Red);
                    return;
                }
                // The actual collection of data
                else if (currentState == ModState.COLLECTING_DATA)
                {
                    try
                    {
                        // We have to move to the next location, check whether we're done
                        if (frameCounter == 0)
                        {
                            MoveToNextLocation();
                            if (currentLocation == locations.Count)
                            {
                                currentState = ModState.DONE;
                                currentLocation = -1;
                                Game.TimeScale = 1.0f;
                                GTA.UI.Notification.Show("All done with data collection!");
                                return;
                            }
                            // Set some time of day
                            locations[currentLocation].CurrentTime = TimesOfDay[random.Next(0, TimesOfDay.Length)];
                            World.CurrentTimeOfDay = locations[currentLocation].CurrentTime;

                            // Set some fps to render at
                            locations[currentLocation].Fps = Fps_Options[random.Next(0, Fps_Options.Length)];
                            frameRenderDelay = (int)Math.Round(1000.0 / locations[currentLocation].Fps);
                            sleepTimeRemainder = Math.Max(1, 50 - frameRenderDelay);

                            // Set some weather
                            locations[currentLocation].CurrentWeather = wantedWeather[random.Next(0, wantedWeather.Length)];
                            World.Weather = locations[currentLocation].CurrentWeather;

                            // Start the game and let it render for some time (to allow weather and time to change)
                            Game.TimeScale = 1.0f;
                            Wait(10000);
                            Game.TimeScale = 0.0f;

                            // Spawn the clusters of peds and allow for some time to get the people settled
                            SpawnGroupsOfPeds();
                            Game.TimeScale = 1.0f;
                            Wait(5000);
                            Game.TimeScale = 0.0f;
                        }

                        // Needed for the vision plugin to not crash
                        Wait(sleepTimeRemainder);

                        // Let game time progress according to FPS
                        Game.TimeScale = 1.0f;
                        Wait(frameRenderDelay);
                        Game.TimeScale = 0.0f;

                        // Record the data, some sleeps are nessecary for vision plugin not to crash!
                        GTAData data = GTAData.DumpPedsData();
                        //Wait(45);

                        stencil = VisionNative.GetStencilBuffer();
                        //Wait(45);

                        // Name of the process GTA5(.exe)
                        color = GrabScreen.ScreenShot("GTA5");
                        Wait(45);

                        depth = VisionNative.GetDepthBuffer();
                        // Make sure we don't have nulls anywhere, if so just return and skip this frame but has never happened for me.
                        if (depth == null || stencil == null || color == null || depth.Length == 0 || stencil.Length == 0 || color.Width == 0 || color.Height == 0)
                        {
                            File.WriteAllText(logFilePath, "Depth null, stencil null, color null" + "\n");
                            Wait(50);
                            return;
                        }

                        // Increment the counters
                        lastImageId++;
                        frameCounter++;

                        // Check if we should "stir the pot"
                        if (frameCounter == 150)
                        {
                            Ped last = null;
                            bool PreviousPedWalking = false;

                            Array values = Enum.GetValues(typeof(PedTaskOptions));

                            foreach (Ped p in spawnedPeds)
                            {
                                PedTaskOptions randomTask = (PedTaskOptions) values.GetValue(random.Next(values.Length));

                                if (random.NextDouble() < 0.3)
                                {
                                    p.Task.WanderAround(p.Position, 15f);
                                    PreviousPedWalking = true;
                                }
                                else if (random.NextDouble() < 0.5)
                                {
                                    p.Task.UseMobilePhone();
                                    PreviousPedWalking = false;
                                }
                                else if (random.NextDouble() < 0.3)
                                {
                                    p.Task.PutAwayMobilePhone();
                                    PreviousPedWalking = false;
                                }
                                else if (last != null && !PreviousPedWalking)
                                {
                                    p.Task.ChatTo(last);
                                    last.Task.ChatTo(p);
                                    PreviousPedWalking = false;
                                }
                                last = p;
                            }
                        }
                        // Check if we should "stir" again and reak heavok on the people
                        else if (frameCounter == EventFrame)
                        {
                            GTALocation tempLocation = locations[currentLocation];
                            // "Fight", "FleeRandom", "FleeSame"
                            tempLocation.Action = Actions[random.Next(0, Actions.Length)];
                            if (tempLocation.Action.Equals("FleeRandom"))
                            {
                                foreach (Ped p in spawnedPeds)
                                {
                                    if (random.NextDouble() < 0.1)
                                    {
                                        p.Task.Cower(10000);
                                    }
                                    else
                                    {
                                        GTAVector groupCenter = tempLocation.PedIdGroup[p.Handle];
                                        p.Task.FleeFrom(new GTA.Math.Vector3(groupCenter.X, groupCenter.Y, groupCenter.Z));
                                    }
                                }
                            }
                            else if (tempLocation.Action.Equals("FleeSame"))
                            {
                                GTAVector groupCenter = tempLocation.PedIdGroup[spawnedPeds[0].Handle];
                                int rand = 0;
                                if (random.NextDouble() < 0.5)
                                {
                                    rand = random.Next(50, 100);
                                }
                                else
                                {
                                    rand = random.Next(-100, -50);
                                }
                                groupCenter.X += rand;
                                if (random.NextDouble() < 0.5)
                                {
                                    rand = random.Next(50, 100);
                                }
                                else
                                {
                                    rand = random.Next(-100, -50);
                                }
                                groupCenter.Y += rand;
                                foreach (Ped p in spawnedPeds)
                                {
                                    p.Task.RunTo(new GTA.Math.Vector3(groupCenter.X, groupCenter.Y, groupCenter.Z));
                                }
                            }
                            else if (tempLocation.Action.Equals("Fight"))
                            {
                                Ped last = null;
                                foreach (Ped p in spawnedPeds)
                                {
                                    // For some reason GTA only ever allows 3 people to fight melee, the rest should just flee
                                    if (last != null && random.NextDouble() < 0.1)
                                    {
                                        p.Task.FightAgainst(last, 10000);
                                        last.Task.FightAgainst(p, 10000);
                                        last = p;
                                    }
                                    else if (last != null)
                                    {
                                        p.Task.ReactAndFlee(last);
                                    }
                                    else
                                    {
                                        p.Task.FleeFrom(p.Position);
                                    }
                                }
                            }
                        }
                        // End of this scene
                        else if (frameCounter == TotalFrames)
                        {
                            try
                            {
                                GTALocation location = locations[currentLocation];
                                string jsonName = Path.Combine(dataPath, "locations_processed/");
                                Directory.CreateDirectory(jsonName);
                                jsonName = Path.Combine(jsonName, location.LocationName + ".json");
                                File.Create(jsonName).Dispose(); // Save the final json of this location to locations_processed
                                using (StreamWriter file = File.AppendText(jsonName))
                                {
                                    file.Write(JsonConvert.SerializeObject(location));
                                }
                            }
                            catch (Exception exception)
                            {
                                File.AppendAllText(logFilePath, "ERROR3: " + exception.ToString() + "\n");
                                File.AppendAllText(logFilePath, "STACK3: " + exception.StackTrace.ToString() + "\n");
                            }
                            DeleteAllSpawned(); // delete all the peds we've spawned
                            frameCounter = 0; // reset the framecounter

                            WriteFiles(data, color, depth, stencil, Path.Combine(locationDir, lastImageId.ToString("D10")), true); // write the final frame and force write=true
                            return;
                        }
                        WriteFiles(data, color, depth, stencil, Path.Combine(locationDir, lastImageId.ToString("D10")));
                    }
                    catch (Exception exception)
                    {
                        File.AppendAllText(logFilePath, "ERROR: " + exception.ToString() + "\n");
                        File.AppendAllText(logFilePath, "STACK: " + exception.StackTrace.ToString() + "\n");
                    }
                }
            }
            catch
            { }
            finally
            {
                // Always set processing to false before returning
                processing = false;
                processing = false;
            }
        }

        // Datastructure to hold information of a single location
        public class GTALocation
        {
            public string LocationName { get; set; }
            public GTAVector CameraPosition { get; set; }
            public GTAVector CameraRotation { get; set; }
            public GTAVector PlayerPosition { get; set; }
            public GTAVector PlayerRotation { get; set; }
            public List<GTAVector> ROI { get; set; }
            public List<List<GTAVector>> PedGroups { get; set; }
            public List<GTAVector> GroupCenters { get; set; }
            public Dictionary<int, GTAVector> PedIdGroup { get; set; }
            public int Fps { get; set; }
            public string Action { get; set; }
            public TimeSpan CurrentTime { get; set; }
            public Weather CurrentWeather { get; set; }
        }

        public void SpawnGroupsOfPeds()
        {
            GTALocation tempLocation = locations[currentLocation];
            tempLocation.PedGroups = new List<List<GTAVector>>();
            tempLocation.GroupCenters = new List<GTAVector>();
            tempLocation.PedIdGroup = new Dictionary<int, GTAVector>();
            GTAVector PedLocation;
            Ped PreviousPed;
            bool PreviousPedWalking;
            int nGroups, groupSize;

            float area = Utils.PolygonArea(tempLocation.ROI); // Area of polygon

            int lowerGroups = Math.Max(1, (int)(area / LowerGroupDiv)); // lower bound of the number of groups
            int lowerGroupSize = Math.Max(MinGroupSize, (int)(area / LowerGroupSizeDiv)); // lower bound of the peds per group

            if (notificationsEnabled) GTA.UI.Notification.Show("Area: " + area + " [" + lowerGroups + ", " + 2 * lowerGroups + "] [" + lowerGroupSize + ", " + 2 * lowerGroupSize + "]");

            nGroups = random.Next(lowerGroups, 2 * lowerGroups);
            for (int n = 0; n < nGroups; n++)
            {
                groupSize = random.Next(lowerGroupSize, 2 * lowerGroupSize);
                GTAVector groupCenter = Utils.RandPointInPoly(tempLocation);
                tempLocation.PedGroups.Add(new List<GTAVector>());
                tempLocation.GroupCenters.Add(groupCenter);
                PreviousPed = null;
                PreviousPedWalking = false;
                int radius = (int)(Math.Sqrt(groupSize / Math.PI) * random.NextDouble(1.2, 2.4));
                for (int i = 0; i < groupSize; i++)
                {
                    // Cap the maximum number of peds we spawn at 100 (GTA has hard coded limit of 256 but this includes all peds that where already in the world)
                    // You can change this limit if you encounter instabilities.
                    if (spawnedPeds.Count > MaxPeds)
                    {
                        return;
                    }
                    // Create a point within radius from the cluster center
                    while (true)
                    {
                        PedLocation = Utils.RandPointInPoly(tempLocation);
                        double dist = Utils.EuclideanDistance(groupCenter, PedLocation);
                        if (dist < radius)
                        {
                            break;
                        }
                    }

                    Ped x = World.CreateRandomPed(new GTA.Math.Vector3(PedLocation.X, PedLocation.Y, PedLocation.Z));
                    // Put the spawned ped on the ground
                    var a = x.Position;
                    a.Z -= x.HeightAboveGround;
                    x.Position = a;
                    // Give random orientation to the ped
                    a = x.Rotation;
                    a.X = 0;
                    a.Y = 0;
                    a.Z = random.NextFloat(-180, 180);
                    x.Heading = a.Z;
                    x.Rotation = a;

                    // Add his to a list such that we can clear them later
                    spawnedPeds.Add(x);
                    tempLocation.PedGroups[n].Add(PedLocation);
                    tempLocation.PedIdGroup.Add(x.Handle, groupCenter);
                    // Give ped some task
                    if (random.NextDouble() < 0.8)
                    {
                        x.Task.WanderAround(x.Position, random.Next(10, 15));
                        PreviousPedWalking = true;
                    }
                    else if (PreviousPed != null && !PreviousPedWalking)
                    {
                        PreviousPedWalking = false;
                        x.Task.ChatTo(PreviousPed);
                        PreviousPed.Task.ChatTo(x);
                        PreviousPed = x;
                    }

                }
            }
        }

        // Just spawn peds infront of the player
        public void SpawnRandomPeds()
        {
            for (int i = 0; i < 50; i++)
            {
                Ped x = World.CreateRandomPed(player.Character.GetOffsetPosition(new GTA.Math.Vector3(random.Next(-15, 15), 10 + random.Next(-8, 25), 0)));
                var a = x.Position;
                a.Z -= x.HeightAboveGround;
                x.Position = a;

                a = x.Rotation;
                a.X = random.NextFloat(0, 100);
                a.Y = random.NextFloat(0, 100);
                a.Z = random.NextFloat(0, 100);
                x.Rotation = a;
                //x.FreezePosition = true;


                if (random.NextDouble() < 0.95)
                {
                    if (random.NextDouble() < 0.25)
                    {
                        var pos = x.Position;
                        if (random.NextDouble() < 0.5)
                        {
                            pos.X += random.Next(15, 20);
                        }
                        else
                        {
                            pos.X -= random.Next(15, 20);
                        }

                        if (random.NextDouble() < 0.5)
                        {
                            pos.Y += random.Next(15, 20);
                        }
                        else
                        {
                            pos.Y -= random.Next(15, 20);
                        }

                        x.Task.RunTo(pos);
                    }
                    else
                    {
                        x.Task.WanderAround(x.Position, random.NextFloat(25, 40));
                    }

                }
                else
                {
                    x.Task.UseMobilePhone(50000);
                }
                spawnedPeds.Add(x);
            }
        }

        // Clear map of all peds we spawned
        public void DeleteAllSpawned()
        {
            foreach (Ped ped in spawnedPeds)
            {
                ped.Kill();
                ped.Delete();
            }
            spawnedPeds.Clear();

            foreach (Vehicle v in spawnedVehicles)
            {
                v.Delete();
            }
            spawnedVehicles.Clear();
        }

        // Shuffle a list
        public void Shuffle<T>(List<T> list)
        {
            int count = list.Count;
            for (var i = 0; i < count - 1; i++)
            {
                var temp = list[i];
                int x = random.Next(i, count);
                list[i] = list[x];
                list[x] = temp;
            }
        }

        // Load previously created locations
        public void LoadLocations()
        {
            locations.Clear();

            rootDir = Path.Combine(dataPath, "data");
            processedDir = Path.Combine(dataPath, "locations_processed");
            Directory.CreateDirectory(processedDir);
            string locationsDir = Path.Combine(dataPath, "locations");

            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(locationsDir);
            foreach (string location in Directory.GetFiles(locationsDir))
            {
                if (location.EndsWith(".json") && !location.StartsWith("."))
                {
                    GTALocation tempLocation = JsonConvert.DeserializeObject<GTALocation>(File.ReadAllText(Path.Combine(locationsDir, location)));
                    try
                    {
                        LocationCounter = Math.Max(LocationCounter, int.Parse(tempLocation.LocationName));
                    }
                    catch { }
                    locations.Add(tempLocation);
                }
            }
            foreach (string location in Directory.GetFiles(processedDir))
            {
                if (location.EndsWith(".json") && !location.StartsWith("."))
                {
                    GTALocation tempLocation = JsonConvert.DeserializeObject<GTALocation>(File.ReadAllText(Path.Combine(locationsDir, location)));
                    processed.Add(tempLocation.LocationName);
                }
            }
            if (notificationsEnabled)
            {
                GTA.UI.Notification.Show("Succesfully loaded " + locations.Count + " locations. " + processed.Count + " already processed.");
            }
        }
        // Save all locations
        public void SaveLocations()
        {
            foreach (GTALocation location in locations)
            {
                try
                {
                    string jsonName = Path.Combine(dataPath, "locations", location.LocationName + ".json");
                    File.Create(jsonName).Dispose();
                    using (StreamWriter file = File.AppendText(jsonName))
                    {
                        file.Write(JsonConvert.SerializeObject(location));
                    }
                }
                catch (Exception exception)
                {
                    File.AppendAllText(logFilePath, "ERROR2: " + exception.ToString() + "\n");
                    File.AppendAllText(logFilePath, "STACK2: " + exception.StackTrace.ToString() + "\n");
                }
            }
            if (notificationsEnabled) GTA.UI.Notification.Show("Saved all locations to " + Path.Combine(dataPath, "locations"));
        }
        // Draw ROI of current location
        private void DrawCurrentROI(List<GTAVector> markers)
        {
            if (markers.Count == 0)
            {
                return;
            }
            GTAVector last = null;
            foreach (GTAVector v in markers)
            {
                World.DrawMarker(MarkerType.DebugSphere, new GTA.Math.Vector3(v.X, v.Y, v.Z), World.RenderingCamera.Rotation, World.RenderingCamera.Rotation, new GTA.Math.Vector3(0.2f, 0.2f, 0.2f), System.Drawing.Color.Red);
                if (last != null)
                {
                    DrawLine(last, v, System.Drawing.Color.Red);
                }
                last = v;
            }
            DrawLine(last, markers[0], System.Drawing.Color.Red);
        }
        // Draw a line between two 3D points in a color
        public static void DrawLine(GTAVector from, GTAVector to, System.Drawing.Color col)
        {
            Function.Call(Hash.DRAW_LINE, from.X, from.Y, from.Z, to.X, to.Y, to.Z, col.R, col.G, col.B, col.A);
        }
        // Handle for key inputs
        public void OnKeyDown(object o, KeyEventArgs k)
        {
            if (k.KeyCode == Keys.Z)
            {
                if (notificationsEnabled)
                {
                    GTA.UI.Notification.Show("Notifications Disabled");
                    notificationsEnabled = false;
                }
                else
                {
                    GTA.UI.Notification.Show("Notifications Enabled");
                    notificationsEnabled = true;

                }
                return;
            }

            switch (this.currentState)
            {
                case ModState.INIT:
                    if (k.KeyCode == Keys.PageUp)
                    {
                        this.Tick += new EventHandler(this.OnTick);
                        LoadLocations();
                        this.currentState = ModState.CHOOSE_LOCATION;
                        GTA.UI.Notification.Show("GTA5Event activated.\nPress F3 to enter free cam. (REQUIRED)");
                    }
                    return;
                case ModState.CHOOSE_LOCATION:
                    if (k.KeyCode == Keys.OemOpenBrackets)
                    {
                        DeleteAllSpawned();
                        if (locations.Count == 0)
                        {
                            GTA.UI.Notification.Show("Locations empty! Please add new location first");
                            return;
                        }
                        if (currentLocation < 1)
                            currentLocation = locations.Count - 1;
                        else
                            currentLocation--;

                        GTALocation tempLocation = locations[currentLocation];
                        player.Character.Position = new GTA.Math.Vector3(tempLocation.PlayerPosition.X, tempLocation.PlayerPosition.Y, tempLocation.PlayerPosition.Z);
                        player.Character.Rotation = new GTA.Math.Vector3(tempLocation.PlayerRotation.X, tempLocation.PlayerRotation.Y, tempLocation.PlayerRotation.Z);
                        World.RenderingCamera.Position = new GTA.Math.Vector3(tempLocation.CameraPosition.X, tempLocation.CameraPosition.Y, tempLocation.CameraPosition.Z);
                        World.RenderingCamera.Rotation = new GTA.Math.Vector3(tempLocation.CameraRotation.X, tempLocation.CameraRotation.Y, tempLocation.CameraRotation.Z);

                        //if (notificationsEnabled) GTA.UI.Notification.Show("Area: " + Utils.PolygonArea(tempLocation.ROI));
                    }
                    else if (k.KeyCode == Keys.OemCloseBrackets)
                    {
                        DeleteAllSpawned();
                        if (locations.Count == 0)
                        {
                            GTA.UI.Notification.Show("Locations empty! Please add new location first");
                            return;
                        }

                        currentLocation = (currentLocation + 1) % locations.Count;

                        GTALocation tempLocation = locations[currentLocation];
                        player.Character.Position = new GTA.Math.Vector3(tempLocation.PlayerPosition.X, tempLocation.PlayerPosition.Y, tempLocation.PlayerPosition.Z);
                        player.Character.Rotation = new GTA.Math.Vector3(tempLocation.PlayerRotation.X, tempLocation.PlayerRotation.Y, tempLocation.PlayerRotation.Z);
                        World.RenderingCamera.Position = new GTA.Math.Vector3(tempLocation.CameraPosition.X, tempLocation.CameraPosition.Y, tempLocation.CameraPosition.Z);
                        World.RenderingCamera.Rotation = new GTA.Math.Vector3(tempLocation.CameraRotation.X, tempLocation.CameraRotation.Y, tempLocation.CameraRotation.Z);
                        //if (notificationsEnabled) GTA.UI.Notification.Show("Area: " + Utils.PolygonArea(tempLocation.ROI));
                    }
                    else if (k.KeyCode == Keys.L)
                    {
                        LocationCounter++;
                        GTALocation tempLocation = new GTALocation
                        {
                            PlayerPosition = new GTAVector(player.Character.Position),
                            PlayerRotation = new GTAVector(player.Character.Rotation),
                            CameraPosition = new GTAVector(World.RenderingCamera.Position),
                            CameraRotation = new GTAVector(World.RenderingCamera.Rotation),
                            ROI = new List<GTAVector>(),
                        };
                        string name = Game.GetUserInput();
                        if (name.Equals(""))
                        {
                            if (notificationsEnabled) GTA.UI.Notification.Show("Aborted; No new location was created.");
                            return;
                        }
                        if (notificationsEnabled) GTA.UI.Notification.Show("Added current location [" + name + "] as new location");
                        tempLocation.LocationName = name;

                        currentLocation = locations.Count;
                        locations.Add(tempLocation);

                    }
                    break;
                case ModState.CHOOSE_ROI:
                    if (k.KeyCode == Keys.U)
                    {
                        if (locations.Count == 0)
                        {
                            GTA.UI.Notification.Show("Locations empty! Please add new location first");
                            return;
                        }
                        if (notificationsEnabled) GTA.UI.Notification.Show("Added current location as new ROI point, resetting player to camera location.");
                        GTALocation tempLocation = locations[currentLocation];
                        //var pos = new GTAVector(player.Character.Position);
                        //pos.Z -= player.Character.HeightAboveGround;
                        tempLocation.ROI.Add(new GTAVector(TempRoiPoint));

                        locationDir = Path.Combine(rootDir, tempLocation.LocationName);

                        if (tempLocation.ROI.Count > 2)
                            GTA.UI.Notification.Show(tempLocation.LocationName + " area: " + Utils.PolygonArea(tempLocation.ROI));
                    }
                    else if (k.KeyCode == Keys.K)
                    {
                        if (locations.Count == 0)
                        {
                            GTA.UI.Notification.Show("Locations empty! Please add new location first");
                            return;
                        }
                        else if (locations[currentLocation].ROI.Count == 0)
                        {
                            GTA.UI.Notification.Show("ROI empty! Please add points to the ROI first");
                            return;
                        }
                        GTALocation tempLocation = locations[currentLocation];
                        tempLocation.ROI.RemoveAt(locations[currentLocation].ROI.Count - 1);
                        if (tempLocation.ROI.Count > 2)
                            GTA.UI.Notification.Show(tempLocation.LocationName + " area: " + Utils.PolygonArea(tempLocation.ROI));
                    }
                    else if (k.KeyCode == Keys.R)
                    {
                        if (locations.Count == 0)
                        {
                            GTA.UI.Notification.Show("Locations empty! Please add new location first");
                            return;
                        }
                        GTALocation tempLocation = locations[currentLocation];
                        player.Character.Position = new GTA.Math.Vector3(tempLocation.PlayerPosition.X, tempLocation.PlayerPosition.Y, tempLocation.PlayerPosition.Z);
                        player.Character.Rotation = new GTA.Math.Vector3(tempLocation.PlayerRotation.X, tempLocation.PlayerRotation.Y, tempLocation.PlayerRotation.Z);
                        World.RenderingCamera.Position = new GTA.Math.Vector3(tempLocation.CameraPosition.X, tempLocation.CameraPosition.Y, tempLocation.CameraPosition.Z);
                        World.RenderingCamera.Rotation = new GTA.Math.Vector3(tempLocation.CameraRotation.X, tempLocation.CameraRotation.Y, tempLocation.CameraRotation.Z);
                    }
                    break;
                case ModState.COLLECTING_DATA:
                    break;
                case ModState.MESS_AROUND:
                    if (k.KeyCode == Keys.OemQuotes)
                    {
                        GTA.UI.Notification.Show("raycast Anything?" + TempRayCastResult.DidHit + " material?" + TempRayCastResult.MaterialHash);
                        GTA.UI.Notification.Show("raycast Result:" + TempRayCastResult.Result + " Entity:" + TempRayCastResult.HitEntity);
                        GTA.UI.Notification.Show("result Point: " + TempRoiPoint);

                        Array values = Enum.GetValues(typeof(PedTaskOptions));
                        PedTaskOptions randomAction = (PedTaskOptions)values.GetValue(spawnedPeds.Count);

                        GTA.UI.Notification.Show(randomAction.ToString());

                        Ped x = World.CreateRandomPed(player.Character.GetOffsetPosition(new GTA.Math.Vector3(0, 15, 0)));
                        var a = x.Position;
                        a.Z -= x.HeightAboveGround;
                        x.Position = a;

                        spawnedPeds.Add(x);

                        x.Task.StartScenario(randomAction.ToString(), 0f);
                        
                        //player.Character.Task.StartScenario(randomAction.ToString(), 0f);

                    }
                    else if (k.KeyCode == Keys.K)
                    {
                        DeleteAllSpawned();
                    }
                    else if (k.KeyCode == Keys.OemSemicolon)
                    {
                        SpawnRandomPeds();
                        GTA.UI.Notification.Show("Total Peds: " + (World.GetNearbyPeds(player.Character.Position, 500f).Length) + " spawned: " + spawnedPeds.Count);

                        // Needed for the vision plugin to not crash
                        Wait(sleepTimeRemainder);

                        // Let game time progress according to FPS
                        Game.TimeScale = 1.0f;
                        Wait(frameRenderDelay);
                        Game.TimeScale = 0.0f;

                        // Record the data, some sleeps are nessecary for vision plugin not to crash!
                        GTAData data = GTAData.DumpEntityData();
                        Wait(45);

                        // Name of the process GTA5(.exe)
                        color = GrabScreen.ScreenShot("GTA5");
                        Wait(45);

                        depth = VisionNative.GetDepthBuffer();
                        Wait(45);

                        stencil = VisionNative.GetStencilBuffer();
                        Game.TimeScale = 1.0f;
                        DeleteAllSpawned();
                        WriteFiles(data, color, depth, stencil, "F:/datasets/GTA_V_anomaly/log_dir/img", true);
                    }
                    break;
            }
            if (k.KeyCode == Keys.F10)
            {
                this.currentState = ModState.CHOOSE_LOCATION;
                GTA.UI.Notification.Show("Go to desired location or use \"[ ]\" to cycle through existing locations. \"L\" to add current view as new location.");
            }
            else if (k.KeyCode == Keys.F11)
            {
                if (locations.Count == 0)
                {
                    GTA.UI.Notification.Show("Locations empty! Please add new location first");
                    GTA.UI.Notification.Show("Go to desired location or use \"[ ]\" to cycle through existing locations. \"L\" to add current view as new location.");
                    this.currentState = ModState.CHOOSE_LOCATION;
                    return;
                }
                this.currentState = ModState.CHOOSE_ROI;

                GTA.UI.Notification.Show("Choose the desired ROI by positioning camera where you want and press \"U\" to update ROI. \"K\" to remove last. \"R\" to reset camera.");
                GTALocation tempLocation = locations[currentLocation];
                if (tempLocation.ROI.Count > 2)
                    GTA.UI.Notification.Show(tempLocation.LocationName + " area: " + Utils.PolygonArea(tempLocation.ROI));

            }
            else if (k.KeyCode == Keys.F12)
            {
                if (locations.Count == 0)
                {
                    GTA.UI.Notification.Show("Locations empty! Please add new location first");
                    return;
                }
                notificationsEnabled = false;
                this.currentState = ModState.COLLECTING_DATA;
                this.Interval = 300;
                Game.TimeScale = 1.0f;
                if (frameCounter == 0)
                    currentLocation = -1;
            }
            else if (k.KeyCode == Keys.N)
            {
                SaveLocations();
            }
            else if (k.KeyCode == Keys.OemPeriod)
            {
                this.currentState = ModState.MESS_AROUND;
            }
        }
    }
}
