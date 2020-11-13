using GTA;
using System;
using System.Collections.Generic;
using System.Linq;
using Vector2 = GTA.Math.Vector2;
using Vector3 = GTA.Math.Vector3;

namespace GTA5Event
{
    public class GTABoundingBox2
    {
        public GTAVector2 Min { get; set; }
        public GTAVector2 Max { get; set; }
        public float Area
        {
            get
            {
                return (Max.X - Min.X) * (Max.Y - Min.Y);
            }
        }
    }

    public class GTABoundingBox
    {
        public GTAVector[] CornerPoints { get; }
        private readonly float x_max = 0, x_min = float.MaxValue, y_max = 0, y_min = float.MaxValue, z_max = 0, z_min = float.MaxValue;
        public GTABoundingBox(GTAVector[] points)
        {
            CornerPoints = points;
            foreach (GTAVector v in points)
            {
                x_max = Math.Max(x_max, v.X);
                x_min = Math.Min(x_min, v.X);

                y_max = Math.Max(y_max, v.Y);
                y_min = Math.Min(y_min, v.Y);

                z_max = Math.Max(z_max, v.Z);
                z_min = Math.Min(z_min, v.Z);
            }
        }

        public GTABoundingBox(Entity e)
        {
            var dim = new GTAVector();
            var e1 = new GTAVector();
            var e2 = new GTAVector();
            var e3 = new GTAVector();
            var e4 = new GTAVector();
            var e5 = new GTAVector();
            var e6 = new GTAVector();
            var e7 = new GTAVector();
            var e8 = new GTAVector();
            (var gmin, var gmax) = e.Model.Dimensions;

            var rightVector = e.ForwardVector;
            var upVector = e.UpVector;
            var forwardVector = e.RightVector;

            var position = e.Position;

            //Calculate siZe
            dim.X = 0.5f * (gmax.X - gmin.X);
            dim.Y = 0.5f * (gmax.Y - gmin.Y);
            dim.Z = 0.5f * (gmax.Z - gmin.Z);

            e1.X = position.X - dim.Y * rightVector.X - dim.X * forwardVector.X - dim.Z * upVector.X;
            e1.Y = position.Y - dim.Y * rightVector.Y - dim.X * forwardVector.Y - dim.Z * upVector.Y;
            e1.Z = position.Z - dim.Y * rightVector.Z - dim.X * forwardVector.Z - dim.Z * upVector.Z;

            e2.X = e1.X + 2 * dim.Y * rightVector.X;
            e2.Y = e1.Y + 2 * dim.Y * rightVector.Y;
            e2.Z = e1.Z + 2 * dim.Y * rightVector.Z;

            e3.X = e2.X + 2 * dim.Z * upVector.X;
            e3.Y = e2.Y + 2 * dim.Z * upVector.Y;
            e3.Z = e2.Z + 2 * dim.Z * upVector.Z;

            e4.X = e1.X + 2 * dim.Z * upVector.X;
            e4.Y = e1.Y + 2 * dim.Z * upVector.Y;
            e4.Z = e1.Z + 2 * dim.Z * upVector.Z;

            e5.X = position.X + dim.Y * rightVector.X + dim.X * forwardVector.X + dim.Z * upVector.X;
            e5.Y = position.Y + dim.Y * rightVector.Y + dim.X * forwardVector.Y + dim.Z * upVector.Y;
            e5.Z = position.Z + dim.Y * rightVector.Z + dim.X * forwardVector.Z + dim.Z * upVector.Z;

            e6.X = e5.X - 2 * dim.Y * rightVector.X;
            e6.Y = e5.Y - 2 * dim.Y * rightVector.Y;
            e6.Z = e5.Z - 2 * dim.Y * rightVector.Z;

            e7.X = e6.X - 2 * dim.Z * upVector.X;
            e7.Y = e6.Y - 2 * dim.Z * upVector.Y;
            e7.Z = e6.Z - 2 * dim.Z * upVector.Z;

            e8.X = e5.X - 2 * dim.Z * upVector.X;
            e8.Y = e5.Y - 2 * dim.Z * upVector.Y;
            e8.Z = e5.Z - 2 * dim.Z * upVector.Z;

            CornerPoints = new GTAVector[] { e1, e2, e3, e4, e5, e5, e6, e7, e8 };

            foreach (GTAVector v in CornerPoints)
            {
                x_max = Math.Max(x_max, v.X);
                x_min = Math.Min(x_min, v.X);

                y_max = Math.Max(y_max, v.Y);
                y_min = Math.Min(y_min, v.Y);

                z_max = Math.Max(z_max, v.Z);
                z_min = Math.Min(z_min, v.Z);
            }
        }

        public bool Contains(GTAVector p)
        {
            return (p.X <= x_max && p.X >= x_min) && (p.Y <= y_max && p.Y >= y_min) && (p.Z <= z_max && p.Z >= z_min);
        }

        public bool Contains(Vector3 p)
        {
            return (p.X <= x_max && p.X >= x_min) && (p.Y <= y_max && p.Y >= y_min) && (p.Z <= z_max && p.Z >= z_min);
        }

        public GTABoundingBox2 To2D()
        {
            float x_min = float.MaxValue, x_max = 0, y_min = float.MaxValue, y_max = 0;
            foreach (GTAVector v in CornerPoints)
            {
                Vector2 temp = HashFunctions.Convert3dTo2d(v, out bool success);
                if (success)
                {
                    x_max = Math.Max(x_max, temp.X);
                    x_min = Math.Min(x_min, temp.X);

                    y_max = Math.Max(y_max, temp.Y);
                    y_min = Math.Min(y_min, temp.Y);
                }
                else
                {
                    System.IO.File.AppendAllText("F:/datasets/GTA_V_anomaly/log.txt", "Coord failed: " + temp.ToString() + "\n");
                }
            }

            x_max = Math.Min(x_max, 1f);
            x_min = Math.Max(x_min, 0f);

            y_max = Math.Min(y_max, 1f);
            y_min = Math.Max(y_min, 0f);

            var result = new GTABoundingBox2
            {
                Min = new GTAVector2(x_min, y_min),
                Max = new GTAVector2(x_max, y_max)
            };
            return result;
        }
    }

    public enum DetectionType
    {
        background,
        person,
        car,
        bike,
        bicycle,
        entity
    }

    public class GTADetection
    {
        public DetectionType Type { get; set; }
        public float Distance { get; set; }
        public int Handle { get; set; }
        public GTABoundingBox2 Bbox2d { get; set; }
        public Dictionary<string, GTAVector2> Bones { get; set; }
        public GTADetection(Entity e, DetectionType type)
        {
            Type = type;
            Distance = World.RenderingCamera.Position.DistanceTo(e.Position);
            Handle = e.Handle;
            Bones = new Dictionary<string, GTAVector2>();
            //(var gmin, var gmax) = e.Model.Dimensions;
            GTABoundingBox bbox = new GTABoundingBox(e);
            Bbox2d = bbox.To2D();
            foreach (EntityBone bone in e.Bones)
            {
                try
                {
                    if (bbox.Contains(bone.Position))
                    {
                        var coord2d = HashFunctions.Convert3dTo2d(bone.Position, out bool success);
                        if (success)
                        {
                            Bones.Add(bone.Index.ToString(), new GTAVector2(coord2d));
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        public GTADetection(Ped p) : this(p, DetectionType.person) { }
        public GTADetection(Vehicle v) : this(v, DetectionType.car) { }
        public static GTADetection CreateDetection(Entity e)
        {
            if (e is Ped p) 
                return new GTADetection(p, DetectionType.person);
            else if (e is Vehicle v)
            {
                if (v.Model.IsBicycle)
                    return new GTADetection(v, DetectionType.bicycle);
                if (v.Model.IsBike)
                    return new GTADetection(v, DetectionType.bike);
                return new GTADetection(v, DetectionType.car);
            }
            else return new GTADetection(e, DetectionType.entity);
        }

    }
    public class GTAVector
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public GTAVector()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }
        public GTAVector(Vector3 v)
        {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
        }

        public GTAVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static explicit operator SharpDX.Vector3(GTAVector i)
        {
            return new SharpDX.Vector3(i.X, i.Y, i.Z);
        }

        public static GTAVector Lerp(GTAVector from, GTAVector to, float by)
        {
            var x = from.X * (1 - by) + to.X * by;
            var y = from.Y * (1 - by) + to.Y * by;
            var z = from.Z * (1 - by) + to.Z * by;
            return new GTAVector(x, y, z);
        }
    }

    public class GTAVector2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public GTAVector2(float x, float y)
        {
            X = x;
            Y = y;
        }
        public GTAVector2()
        {
            X = 0f;
            Y = 0f;
        }
        public GTAVector2(Vector2 v)
        {
            X = v.X;
            Y = v.Y;
        }
    }

    public class GTAData
    {
        public List<GTADetection> Detections { get; set; }
        public static GTAData DumpPedsData()
        {
            var ret = new GTAData();
            Ped[] peds = World.GetNearbyPeds(World.RenderingCamera.Position, 500f);
            var pedList = from ped in peds
                          where ped != null && ped.IsHuman && ped.IsVisible && !ped.IsOccluded
                          select new GTADetection(ped);
            ret.Detections = new List<GTADetection>();
            foreach (GTADetection d in pedList.ToList())
            {
                if (d.Bones.Count > 3)
                    ret.Detections.Add(d);
            }
            return ret;
        }

        public static void ShowBoundingBoxes()
        {
            //var entities = World.GetNearbyEntities(World.RenderingCamera.Position, 200f);
            var entities = World.GetNearbyEntities(Game.Player.Character.Position, 200f);
            var entityList = from entity in entities
                                 //where entity != null && entity.IsOnScreen && !entity.IsOccluded && (entity.Model.IsBicycle || entity.Model.IsBike || entity.Model.IsVehicle || entity.Model.IsPed)
                             where entity != null && (entity.Model.IsBicycle || entity.Model.IsBike || entity.Model.IsVehicle || entity.Model.IsPed)
                             select entity;
            foreach (Entity e in entityList)
            {
                //byte green, red;
                //var x = HashFunctions.HAS_ENTITY_CLEAR_LOS_TO_ENTITY(Game.Player.Character, e);
                ////GTA.UI.Notification.Show("" + x);
                //if (x)
                //{
                //    continue;
                //    green = 255;
                //    red = 0;
                //}
                //else
                //{
                //    green = 0;
                //    red = 255;
                //}
                var boundingBox = new GTABoundingBox(e);
                var corners = boundingBox.CornerPoints;
                for (int i = 0; i < corners.Length; ++i)
                {
                    for (int j = 0; j < corners.Length; ++j)
                    {
                        if (j == i) continue;
                        var c1 = corners[i];
                        var c2 = corners[j];
                        HashFunctions.Draw3DLine(c1, c2, 0, 0);
                    }
                }
                //var bbox2d = boundingBox.To2D();
                //var w = bbox2d.Max.X - bbox2d.Min.X;
                //var h = bbox2d.Max.Y - bbox2d.Min.Y;

                //HashFunctions.DrawRect(bbox2d.Min.X + w/2f, bbox2d.Min.Y + h/2f, w, h, red, green, 0,255);
            }
        }

        public static GTAData DumpEntityData() // TODO FIX THIS!
        {
            var ret = new GTAData();
            //var constants = VisionNative.GetConstants();
            //if (!constants.HasValue) return null;
            //var W = MathNet.Numerics.LinearAlgebra.Single.DenseMatrix.OfColumnMajor(4, 4, constants.Value.world.ToArray()).ToDouble();
            //var WV =
            //    MathNet.Numerics.LinearAlgebra.Single.DenseMatrix.OfColumnMajor(4, 4,
            //        constants.Value.worldView.ToArray()).ToDouble();
            //var WVP =
            //    MathNet.Numerics.LinearAlgebra.Single.DenseMatrix.OfColumnMajor(4, 4,
            //        constants.Value.worldViewProjection.ToArray()).ToDouble();
            ////constants.Value.worldViewProjection.Invert();

            //var V = WV * W.Inverse();
            //var P = WVP * WV.Inverse();
            //ret.ProjectionMatrix = P as DenseMatrix;
            //ret.ViewMatrix = V as DenseMatrix;
            //ret.WorldMatrix = W as DenseMatrix;

            var entities = World.GetNearbyEntities(Game.Player.Character.Position, 200f);
            var entityList = from entity in entities
                             where entity != null && (entity.Model.IsBicycle || entity.Model.IsBike || entity.Model.IsVehicle || entity.Model.IsPed)
                             select GTADetection.CreateDetection(entity);

            //var entities = World.GetNearbyEntities(World.RenderingCamera.Position, 200f);

            //var entityList = from entity in entities
            //                 where entity != null && entity.IsOnScreen && (entity.Model.IsBicycle || entity.Model.IsBike || entity.Model.IsVehicle || entity.Model.IsPed) && HashFunctions.HAS_ENTITY_CLEAR_LOS_TO_ENTITY(Game.Player.Character, entity)
            //                 select GTADetection.CreateDetection(entity);

            ret.Detections = new List<GTADetection>();
            foreach (GTADetection d in entityList)
            {
                if (d.Bones.Count > 3)
                {
                    ret.Detections.Add(d);
                }
            }
            return ret;
        }
    }
}