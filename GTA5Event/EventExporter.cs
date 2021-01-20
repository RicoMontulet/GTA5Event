using GTA;
using GTA.Native;
using IniParser;
using Newtonsoft.Json;
using SharpDX;
using System;
using IniParser.Model;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Globalization;
using MathNet.Numerics.LinearAlgebra.Double;

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
        private bool NotificationsEnabled = true;
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
        private readonly int FirstEventFrame;
        private readonly int SecondEventFrame;
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
        private static Dictionary<string, object> Parameters = new Dictionary<string, object>();

        public EventExporter()
        {
            GTA.UI.Screen.ShowHelpTextThisFrame(Directory.GetCurrentDirectory() + "/Scripts/gta_config.ini");
            var parser = new FileIniDataParser();
            var data = parser.ReadFile(Directory.GetCurrentDirectory() + "/Scripts/gta_config.ini");

            dataPath = data["directories"]["location_dir"];
            if (!Directory.Exists(dataPath)) 
                Directory.CreateDirectory(dataPath);

            logFilePath = data["directories"]["log_dir"];

            File.WriteAllText(logFilePath, "EventExporter constructor called.\n");
            File.AppendAllText(logFilePath, "Resolution: " + screenResolution + "\n");

            // Parse config

            ParseIntegerParams(data);
            ParseFloatParams(data);
            ParseBooleanParams(data);
            ParseListOfIntegerParams(data);
            ParseListOfStringParams(data);
            ParseTimestampParams(data);


            LowerGroupDiv = (int) Parameters["LowerGroupDiv"];
            LowerGroupSizeDiv = (int) Parameters["LowerGroupSizeDiv"];
            MinGroupSize = (int) Parameters["MinGroupSize"];
            MaxPeds = (int) Parameters["MaxPeds"];
            TotalFrames = (int) Parameters["TotalFrames"];
            FirstEventFrame = (int) Parameters["FirstEventFrame"];
            SecondEventFrame = (int) Parameters["SecondEventFrame"];
            
            if (FirstEventFrame > TotalFrames)
            {
                GTA.UI.Notification.Show("Warning! FirstEventFrame > TotalFrames so event will never happen!");
            }
            if (FirstEventFrame < 0)
            {
                GTA.UI.Notification.Show("Warning! FirstEventFrame < 0 event will happen immediatly!");
            }
            if (SecondEventFrame > TotalFrames)
            {
                GTA.UI.Notification.Show("Warning! SecondEventFrame > TotalFrames so event will never happen!");
            }
            if (SecondEventFrame < 0)
            {
                GTA.UI.Notification.Show("Warning! SecondEventFrame < 0 event will happen immediatly!");
            }

            if (TotalFrames < 1)
            {
                GTA.UI.Notification.Show("Error! Totalframes < 1. Script will crash now");
                throw new Exception("Totalframes < 1");
            }

            NotificationsEnabled = (bool)Parameters["NotificationsEnabled"];

            var weatherOptions = (List<string>)Parameters["Weather"];
            var weatherEnums = new List<Weather>();
            foreach (var w in weatherOptions)
            {
                weatherEnums.Add((Weather)Enum.Parse(typeof(Weather), w));
            }
            wantedWeather = weatherEnums.ToArray();

            Actions = ((List<string>)Parameters["Actions"]).ToArray();

            Fps_Options = ((List<int>)Parameters["Fps"]).ToArray();

            var startTime = (string[])Parameters["StartTime"];
            var endTime = (string[])Parameters["EndTime"];

            if (startTime.Length != 3 || endTime.Length != 3)
            {
                GTA.UI.Notification.Show("Error! Starttime or endtime format is wrong! Should be HH:MM:SS. Script will crash now");
                throw new Exception("Starttime or endtime not of format HH:MM:SS");
            }
            var timeIntervals = (int)Parameters["timeIntervals"];

            var start_timespan = new TimeSpan(int.Parse(startTime[0]), int.Parse(startTime[1]), int.Parse(startTime[2]));
            var end_timespan = new TimeSpan(int.Parse(endTime[0]), int.Parse(endTime[1]), int.Parse(endTime[2]));

            var time_delta = TimeSpan.FromTicks((end_timespan - start_timespan).Ticks / (timeIntervals - 1));

            var time_stamps = new List<TimeSpan>();
            for (int i = 0; i < timeIntervals; i++)
            {
                time_stamps.Add(start_timespan);
                start_timespan += time_delta;
            }
            TimesOfDay = time_stamps.ToArray();

            player = Game.Player;
            this.KeyDown += this.OnKeyDown;
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
            int height = (int)Parameters["Height"];
            int width = (int)Parameters["Width"];
            if (screenResolution.Width != width || screenResolution.Height != height)
            {
                GTA.UI.Notification.Show(
                    "The current game resolution != config res, please change!\n" +
                    "config: " + width + "x" + height + "\n" +
                    "game: " + screenResolution.Width + "x" + screenResolution.Height);
            }
            Game.TimeScale = 1.0f;
        }

        private void ParseIntegerParams(IniData data)
        {
            var config = data["ints"];
            foreach (var key in config)
            {
                Parameters.Add(key.KeyName, int.Parse(key.Value));
                File.AppendAllText(logFilePath, "Parsed int: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
        }
        private void ParseFloatParams(IniData data)
        {
            var config = data["floats"];
            foreach (var key in config)
            {
                Parameters.Add(key.KeyName, float.Parse(key.Value, CultureInfo.InvariantCulture.NumberFormat));
                File.AppendAllText(logFilePath, "Parsed float: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
        }

        private void ParseBooleanParams(IniData data)
        {
            var config = data["bools"];
            foreach (var key in config)
            {
                Parameters.Add(key.KeyName, bool.Parse(key.Value));
                File.AppendAllText(logFilePath, "Parsed bool: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
            }

            private void ParseListOfIntegerParams(IniData data)
        {
            var config = data["listOfInts"];
            foreach (var key in config)
            {
                if (key.Value.Contains(","))
                {
                    var temp = new List<int>();
                    foreach (var w in key.Value.Replace(" ", "").Split(','))
                        temp.Add(int.Parse(w));
                    Parameters.Add(key.KeyName, temp);
                }
                else
                {
                    Parameters.Add(key.KeyName, int.Parse(key.Value));
                }
                File.AppendAllText(logFilePath, "Parsed listofints: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
        }

        private void ParseListOfStringParams(IniData data)
        {
            var config = data["listOfStrings"];
            foreach (var key in config)
            {
                if (key.Value.Contains(","))
                {
                    var temp = new List<string>();
                    foreach (var w in key.Value.Replace(" ", "").Split(','))
                        temp.Add(w);
                    Parameters.Add(key.KeyName, temp);
                }
                else
                {
                    Parameters.Add(key.KeyName, key.Value);
                }
                File.AppendAllText(logFilePath, "Parsed listofstrings: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
        }

        private void ParseTimestampParams(IniData data)
        {
            var config = data["timestamps"];
            foreach (var key in config)
            {
                Parameters.Add(key.KeyName, key.Value.Replace(" ", "").Split(':'));
                File.AppendAllText(logFilePath, "Parsed timestamps: " + key.KeyName + "=" + Parameters[key.KeyName] + "\n");
            }
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
                // recursively skip already processed locations
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
                // Create the directory if it doesn't already exists
                Directory.CreateDirectory(locationDir);
            }
        }

        public void OnTick(object o, EventArgs e)
        {
            // Safety if this takes longer than Interval it might be called again
            if (processing)
            {
                return;
            }
            else
            {
                processing = true;
            }
            // Try is needed for resetting processing in the finally case (I forgot it sometimes, its just to prevent duplicate code)
            try
            {
                // This function won't do anything in these states
                if (currentState == ModState.CHOOSE_LOCATION || currentState == ModState.DONE)
                {
                    return;
                }
                // This is only to try stuff in (probably not in final version)
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
                // This function draws current ROI if it's not empty and projects where the next point will be placed at the end of the cursor and saves it in TempRoiPoint
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
                            // Set some time of day and save it for annotations
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
                            //Game.TimeScale = 0.0f;

                            // Spawn the clusters of peds and allow for some time to get the people settled
                            SpawnGroupsOfPeds();
                            //Game.TimeScale = 1.0f;
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
                        GTAData data = GTAData.DumpEntityData();
                        //Wait(45);

                        stencil = VisionNative.GetStencilBuffer();
                        //Wait(45);

                        // Name of the process GTA5(.exe)
                        color = GrabScreen.ScreenShot("GTA5", screenResolution.Width, screenResolution.Height);
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
                        if (frameCounter == FirstEventFrame)
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
                        else if (frameCounter == SecondEventFrame)
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
                            //
                            //
                            //
                            //
                            // Feel free to add more Actions and implement how people should move/react :)
                            //
                            //
                            //
                            //
                        }
                        // End of this scene
                        else if (frameCounter == TotalFrames)
                        {
                            try
                            {
                                GTALocation location = locations[currentLocation];
                                
                                var constants = VisionNative.GetConstants();
                                var W = DenseMatrix.OfColumnMajor(4, 4, Utils.ToDouble(constants.Value.world.ToArray()));
                                var WV =
                                    DenseMatrix.OfColumnMajor(4, 4, Utils.ToDouble(constants.Value.worldView.ToArray()));
                                var WVP =
                                    DenseMatrix.OfColumnMajor(4, 4, Utils.ToDouble(constants.Value.worldViewProjection.ToArray()));
                                //constants.Value.worldViewProjection.Invert();

                                var V = (WV * W.Inverse()) as DenseMatrix;
                                var P = (WVP * WV.Inverse()) as DenseMatrix;
                                location.ProjectionMatrix = new GTAMatrix(P);
                                location.ViewMatrix = new GTAMatrix(V);
                                location.WorldMatrix = new GTAMatrix(W);
                                

                                location.CameraNearClip = World.RenderingCamera.NearClip;
                                location.CameraFarClip = World.RenderingCamera.FarClip;
                                location.CameraDirection = new GTAVector(World.RenderingCamera.Direction);
                                location.CameraFOV = World.RenderingCamera.FieldOfView;

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
            }
        }

        // Datastructure to hold information of a single location
        public class GTALocation
        {
            public string LocationName { get; set; }
            public GTAVector CameraPosition { get; set; }
            public GTAVector CameraRotation { get; set; }
            public GTAVector CameraDirection { get; set; }
            public float CameraNearClip { get; set; }
            public float CameraFarClip { get; set; }
            public float CameraFOV { get; set; }
            public int Fps { get; set; }
            public GTAVector PlayerPosition { get; set; }
            public GTAVector PlayerRotation { get; set; }
            public List<GTAVector> ROI { get; set; }
            public List<List<GTAVector>> PedGroups { get; set; }
            public List<GTAVector> GroupCenters { get; set; }
            public Dictionary<int, GTAVector> PedIdGroup { get; set; }
            public string Action { get; set; }
            public TimeSpan CurrentTime { get; set; }
            public Weather CurrentWeather { get; set; }
            public GTAMatrix ProjectionMatrix { get; set; }
            public GTAMatrix ViewMatrix { get; set; }
            public GTAMatrix WorldMatrix { get; set; }
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

            float area = Utils.PolygonArea(tempLocation.ROI); // Area of polygon, used to compute number of groups and group sizes

            int lowerNGroups = Math.Max(1, (int)(area / LowerGroupDiv)); // lower bound of the number of groups, upperbound is two times lowerbound
            int lowerGroupSize = Math.Max(MinGroupSize, (int)(area / LowerGroupSizeDiv)); // lower bound of the peds per group, upperbound is two times lowerbound

            if (NotificationsEnabled) GTA.UI.Notification.Show("Area: " + area + " [" + lowerNGroups + ", " + 2 * lowerNGroups + "] [" + lowerGroupSize + ", " + 2 * lowerGroupSize + "]");

            nGroups = random.Next(lowerNGroups, 2 * lowerNGroups);
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
                    // Cap the maximum number of peds we spawn at MaxPeds (GTA has hard coded limit of 256 but this includes all peds that where already in the world)
                    // You can change this limit in config file if you encounter instabilities.
                    if (spawnedPeds.Count > MaxPeds)
                    {
                        return;
                    }
                    // Create a point within radius from the cluster center, since this is run only once per video inefficient code doesnt matter :P
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

                    // Add ped to a list such that we can delete them later
                    spawnedPeds.Add(x);
                    // Save his starting location and group for logging purposes
                    tempLocation.PedGroups[n].Add(PedLocation);
                    tempLocation.PedIdGroup.Add(x.Handle, groupCenter);
                    
                    // Give ped some task

                    // They can use the phone while walking so this is seperate "if"
                    if (random.NextDouble() < (float)Parameters["PhoneProbability"])
                    {
                        x.Task.UseMobilePhone(50000);
                    }

                    if (random.NextDouble() < (float)Parameters["WalkProbability"])
                    {
                        x.Task.WanderAround(x.Position, random.Next((int)(0.5*radius), (int)(3*radius))); // Explore within your groups 0.5*radius and 2*radius, feel free to change
                        PreviousPedWalking = true;
                    }
                    else if (PreviousPed != null && !PreviousPedWalking) // peds can't walk and talk sadly so elif
                    {
                        PreviousPedWalking = false;
                        x.Task.ChatTo(PreviousPed);
                        PreviousPed.Task.ChatTo(x);
                        PreviousPed = x;
                    }
                    //
                    //
                    //
                    //
                    // Feel free to add more Tasks and implement how people should move/react :)
                    //
                    //
                    //
                    //
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
            if (NotificationsEnabled)
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
            if (NotificationsEnabled) GTA.UI.Notification.Show("Saved all locations to " + Path.Combine(dataPath, "locations"));
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
                if (NotificationsEnabled)
                {
                    GTA.UI.Notification.Show("Notifications Disabled");
                    NotificationsEnabled = false;
                }
                else
                {
                    GTA.UI.Notification.Show("Notifications Enabled");
                    NotificationsEnabled = true;

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
                            GTA.UI.Notification.Show("Aborted; No new location was created.");
                            return;
                        }
                        GTA.UI.Notification.Show("Added current location [" + name + "] as new location");
                        foreach (GTALocation other in locations)
                        {
                            if (other.LocationName.Equals(name))
                            {
                                GTA.UI.Notification.Show("Aborted; Location has the same name as existing location!");
                                return;
                            }
                        }
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
                        if (NotificationsEnabled) GTA.UI.Notification.Show("Added current location as new ROI point, resetting player to camera location.");
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
                        color = GrabScreen.ScreenShot("GTA5", screenResolution.Width, screenResolution.Height);
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
                if (this.currentLocation == -1) this.currentLocation = 0;
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
                int height = (int)Parameters["Height"];
                int width = (int)Parameters["Width"];
                if (screenResolution.Width != width || screenResolution.Height != height)
                {
                    GTA.UI.Notification.Show(
                        "The current game resolution != config res Please change before pressing f12 again!\n" +
                        "config: " + width + "x" + height + "\n" +
                        "game: " + screenResolution.Width + "x" + screenResolution.Height);
                    return;
                }
                NotificationsEnabled = false;
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
