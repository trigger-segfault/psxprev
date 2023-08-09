using System;
using System.Diagnostics;
using System.Text;
using OpenTK;

namespace PSXPrev.Common
{
    public static class GeomMath
    {
        public const string FloatFormat = "0.00000";
        public const string IntegerFormat = "0";
        public const string CompleteFloatFormat = "{0:0.00000}";

        public const float One2Rad = (float)(Math.PI * 2d);
        public const float Deg2Rad = (float)((Math.PI * 2d) / 360d);
        public const float Rad2Deg = (float)(360d / (Math.PI * 2d));

        public const float Fixed12Scalar = 4096f;
        public const float Fixed16Scalar = 65536f;

        public static float UVScalar => Program.FixUVAlignment ? 256f : 255f;

        // Use Vector3.Unit(XYZ) fields instead.
        //public static Vector3 XVector = new Vector3(1f, 0f, 0f);
        //public static Vector3 YVector = new Vector3(0f, 1f, 0f);
        //public static Vector3 ZVector = new Vector3(0f, 0f, 1f);

        // Use Vector3.Distance instead.
        //public static float VecDistance(Vector3 a, Vector3 b)
        //{
        //    var x = a.X - b.X;
        //    var y = a.Y - b.Y;
        //    var z = a.Z - b.Z;
        //    return (float)Math.Sqrt((x * x) + (y * y) + (z * z));
        //}

        public static Vector3 ToVector3(this System.Drawing.Color color)
        {
            return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
        }

        public static Vector4 ToVector4(this System.Drawing.Color color)
        {
            return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }


        public static Vector3 TransformNormalNormalized(Vector3 normal, Matrix4 matrix)
        {
            TransformNormalNormalized(ref normal, ref matrix, out var result);
            return result;
        }

        public static void TransformNormalNormalized(ref Vector3 normal, ref Matrix4 matrix, out Vector3 result)
        {
            Matrix4.Invert(ref matrix, out var invMatrix);
            TransformNormalInverseNormalized(ref normal, ref invMatrix, out result);
        }

        public static Vector3 TransformNormalInverseNormalized(Vector3 normal, Matrix4 invMatrix)
        {
            TransformNormalInverseNormalized(ref normal, ref invMatrix, out var result);
            return result;
        }

        public static void TransformNormalInverseNormalized(ref Vector3 normal, ref Matrix4 invMatrix, out Vector3 result)
        {
            Vector3.TransformPosition(Vector3.Zero, Matrix4.Zero);
            Vector3.TransformNormalInverse(ref normal, ref invMatrix, out result);
            if (!result.IsZero())
            {
                result.Normalize();
            }
        }


        // One-liners for help assigning to the same value, while avoiding a struct copy of Matrix4.
        public static Vector3 TransformNormalNormalized(ref Vector3 normal, ref Matrix4 matrix)
        {
            Matrix4.Invert(ref matrix, out var invMatrix);
            return TransformNormalInverseNormalized(ref normal, ref invMatrix);
        }

        public static Vector3 TransformNormalInverseNormalized(ref Vector3 normal, ref Matrix4 invMatrix)
        {
            TransformNormalInverseNormalized(ref normal, ref invMatrix, out var result);
            return result;
        }

        public static Vector3 TransformPosition(ref Vector3 position, ref Matrix4 matrix)
        {
            Vector3.TransformPosition(ref position, ref matrix, out var result);
            return result;
        }


        public static Matrix4 SetRotation(this Matrix4 matrix, Quaternion rotation)
        {
            matrix = matrix.ClearRotation();
            matrix *= Matrix4.CreateFromQuaternion(rotation);
            return matrix;
        }

        public static Matrix4 SetScale(this Matrix4 matrix, Vector3 scale)
        {
            matrix = matrix.ClearScale();
            matrix *= Matrix4.CreateScale(scale);
            return matrix;
        }

        public static Matrix4 SetTranslation(this Matrix4 matrix, Vector3 translation)
        {
            matrix = matrix.ClearTranslation();
            matrix *= Matrix4.CreateTranslation(translation);
            return matrix;
        }

        public static string WriteVector3(Vector3? v)
        {
            var stringBuilder = new StringBuilder();
            if (v != null)
            {
                var vr = (Vector3)v;
                stringBuilder.AppendFormat(CompleteFloatFormat, vr.X).Append(", ").AppendFormat(CompleteFloatFormat, vr.Y).Append(", ").AppendFormat(CompleteFloatFormat, vr.Z);
            }
            return stringBuilder.ToString();
        }

        public static object WriteIntArray(int[] intArray)
        {
            var stringBuilder = new StringBuilder();
            for (var i = 0; i < intArray.Length; i++)
            {
                if (i > 0)
                {
                    stringBuilder.Append(", ");
                }
                var item = intArray[i];
                stringBuilder.Append(item);
            }
            return stringBuilder.ToString();
        }

        public static Matrix4 CreateRotation(Vector3 rotation)
        {
            // todo: Should this actually be RotationOrder.XYZ (zRot * yRot * xRot)?
            return CreateRotation(rotation, RotationOrder.ZYX); // RotationOrder.XYZ);
        }

        public static Matrix4 CreateRotation(Vector3 rotation, RotationOrder order)
        {
            var xRot = Matrix4.CreateRotationX(rotation.X);
            var yRot = Matrix4.CreateRotationY(rotation.Y);
            var zRot = Matrix4.CreateRotationZ(rotation.Z);
            switch (order)
            {
                case RotationOrder.XYZ: return zRot * yRot * xRot;
                case RotationOrder.XZY: return yRot * zRot * xRot;
                case RotationOrder.YXZ: return zRot * xRot * yRot;
                case RotationOrder.YZX: return xRot * zRot * yRot;
                case RotationOrder.ZXY: return yRot * xRot * zRot;
                case RotationOrder.ZYX: return xRot * yRot * zRot;
            }
            return Matrix4.Identity; // Invalid rotation order
        }

        // Avoid getting NaN quaternions when any matrix scale dimension is 0.
        public static Quaternion ExtractRotationSafe(this Matrix4 matrix)
        {
            var rotation = matrix.ExtractRotation();
            return float.IsNaN(rotation.X) ? Quaternion.Identity : rotation;
        }

        public static Vector3 UnProject(this Vector3 position, Matrix4 projection, Matrix4 view, float width, float height)
        {
            // Not entirely sure if the -1 in `height - 1f - position.Y` should be there or not.
            // For now, intersections with the gizmo seem slightly more accurate with the -1.

            // OpenTK version:
            //var viewProjInv = Matrix4.Invert(view * projection);
            //position.Y = height - 1f - position.Y;
            //return Vector3.Unproject(position, 0f, 0f, width, height, 0f, 1f, viewProjInv);

            Vector4 vec;
            vec.X = 2.0f * position.X / width - 1;
            vec.Y = 2.0f * (height - 1f - position.Y) / height - 1;
            vec.Z = 2.0f * position.Z - 1; // 2.0f * position.Z / depth - 1; // Where depth=1
            vec.W = 1.0f;
            var viewInv = Matrix4.Invert(view);
            var projInv = Matrix4.Invert(projection);
            Vector4.Transform(ref vec, ref projInv, out vec);
            Vector4.Transform(ref vec, ref viewInv, out vec);
            if (Math.Abs(vec.W) > 0.0000000001f) //0.000001f)
            {
                vec.X /= vec.W;
                vec.Y /= vec.W;
                vec.Z /= vec.W;
            }
            return vec.Xyz;
        }

        public static Vector3 ProjectOnNormal(this Vector3 vector, Vector3 normal)
        {
            var num = normal.LengthSquared;
            if (num < float.Epsilon) // The same as num <= 0f
            {
                return Vector3.Zero;
            }
            return normal * Vector3.Dot(vector, normal) / num;
        }

        // Useful for BoxIntersect so that we can still operate on an axis-aligned box.
        public static void TransformRay(Vector3 rayOrigin, Vector3 rayDirection, Matrix4 matrix, out Vector3 resultOrigin, out Vector3 resultDirection)
        {
            var invMatrix = Matrix4.Invert(matrix);
            TransformRayInverse(rayOrigin, rayDirection, invMatrix, out resultOrigin, out resultDirection);
        }

        public static void TransformRayInverse(Vector3 rayOrigin, Vector3 rayDirection, Matrix4 invMatrix, out Vector3 resultOrigin, out Vector3 resultDirection)
        {
            Vector3.TransformPosition(ref rayOrigin, ref invMatrix, out resultOrigin);
            TransformNormalInverseNormalized(ref rayDirection, ref invMatrix, out resultDirection);
            if (!resultDirection.IsZero())
            {
                resultDirection.Normalize();
            }
        }

        public static float BoxIntersect(Vector3 rayOrigin, Vector3 rayDirection, Vector3 boxMin, Vector3 boxMax)
        {
            var t1 = (boxMin.X - rayOrigin.X) / rayDirection.X;
            var t2 = (boxMax.X - rayOrigin.X) / rayDirection.X;
            var t3 = (boxMin.Y - rayOrigin.Y) / rayDirection.Y;
            var t4 = (boxMax.Y - rayOrigin.Y) / rayDirection.Y;
            var t5 = (boxMin.Z - rayOrigin.Z) / rayDirection.Z;
            var t6 = (boxMax.Z - rayOrigin.Z) / rayDirection.Z;

            var aMin = t1 < t2 ? t1 : t2;
            var bMin = t3 < t4 ? t3 : t4;
            var cMin = t5 < t6 ? t5 : t6;

            var aMax = t1 > t2 ? t1 : t2;
            var bMax = t3 > t4 ? t3 : t4;
            var cMax = t5 > t6 ? t5 : t6;

            var fMax = aMin > bMin ? aMin : bMin;
            var fMin = aMax < bMax ? aMax : bMax;

            var t7 = fMax > cMin ? fMax : cMin;
            var t8 = fMin < cMax ? fMin : cMax;

            var t9 = (t8 < 0 || t7 > t8) ? -1 : t7;

            return t9;
        }

        public static Vector3 PlaneIntersect(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planeOrigin, Vector3 planeNormal)
        {
            var diff = rayOrigin - planeOrigin;
            var prod1 = Vector3.Dot(diff, planeNormal);
            var prod2 = Vector3.Dot(rayDirection, planeNormal);
            var intersectionDistance = -prod1 / prod2;
            return rayOrigin + rayDirection * intersectionDistance;
        }

        public static float TriangleIntersect(Vector3 rayOrigin, Vector3 rayDirection, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, bool front = true, bool back = true)
        {
            // Find plane normal, intersection, and intersection distance.
            // We don't need to normalize this.
            var planeNormal = Vector3.Cross((vertex1 - vertex0), (vertex2 - vertex0));

            // Find distance to intersection.
            var diff = rayOrigin - vertex0;
            var prod1 = Vector3.Dot(diff, planeNormal);
            var prod2 = Vector3.Dot(rayDirection, planeNormal);
            if (Math.Abs(prod2) <= 0.0000000001f) //0.000001f)
            {
                return -1f; // Ray and plane are parallel.
            }
            if ((!front && prod2 > 0f) || (!back && prod2 < 0f))
            {
                return -1f; // Ray intersects from a side we don't want to intersect with.
            }
            var intersectionDistance = -prod1 / prod2;
            if (intersectionDistance < 0f)
            {
                return -1f; // Triangle is behind the ray.
            }
            
            var planeIntersection = rayOrigin + rayDirection * intersectionDistance;


            // Perform inside-outside test. Dot product is less than 0 if planeIntersection lies outside of the edge.
            var edge0 = vertex1 - vertex0;
            var C0 = planeIntersection - vertex0;
            if (Vector3.Dot(Vector3.Cross(edge0, C0), planeNormal) < 0f)
            {
                return -1f;
            }
            var edge1 = vertex2 - vertex1;
            var C1 = planeIntersection - vertex1;
            if (Vector3.Dot(Vector3.Cross(edge1, C1), planeNormal) < 0f)
            {
                return -1f;
            }
            var edge2 = vertex0 - vertex2;
            var C2 = planeIntersection - vertex2;
            if (Vector3.Dot(Vector3.Cross(edge2, C2), planeNormal) < 0f)
            {
                return -1f;
            }

            return intersectionDistance;
        }

        public static void GetBoxMinMax(Vector3 center, Vector3 size, out Vector3 outMin, out Vector3 outMax, Matrix4? matrix = null)
        {
            var min = new Vector3(center.X - size.X, center.Y - size.Y, center.Z - size.Z);
            var max = new Vector3(center.X + size.X, center.Y + size.Y, center.Z + size.Z);
            if (matrix.HasValue)
            {
                var matrixValue = matrix.Value;
                Vector3.TransformPosition(ref min, ref matrixValue, out outMin);
                Vector3.TransformPosition(ref max, ref matrixValue, out outMax);
            }
            else
            {
                outMin = min;
                outMax = max;
            }
        }

        public static bool IsZero(this Vector3 v)
        {
            return (v.X == 0f && v.Y == 0f && v.Z == 0f);
            // Or just: return v == Vector3.Zero;
        }

        public static Vector3 CalculateNormal(Vector3 vertex0, Vector3 vertex1, Vector3 vertex2)
        {
            var cross = Vector3.Cross(vertex2 - vertex0, vertex1 - vertex0);
            if (!cross.IsZero())
            {
                cross.Normalize();
            }
            return cross;
        }

        // Shift the components of vector by axis amount.
        // When axis = 0 (X): vector is unchanged.
        // When axis = 1 (Y): vector.X -> Y, vector.Y -> Z, vector.Z -> X.
        // When axis = 2 (Z): vector.X -> Z, vector.Y -> X, vector.Z -> Y.
        public static Vector3 SwapAxes(int axis, Vector3 vector)
        {
            switch (axis)
            {
                case 0: return vector;
                case 1: return new Vector3(vector.Z, vector.X, vector.Y);
                case 2: return new Vector3(vector.Y, vector.Z, vector.X);
            }
            throw new IndexOutOfRangeException(nameof(axis) + " must be between 0 and 2");
        }

        public static Vector3 SwapAxes(int axis, float x, float y, float z)
        {
            switch (axis)
            {
                case 0: return new Vector3(x, y, z);
                case 1: return new Vector3(z, x, y);
                case 2: return new Vector3(y, z, x);
            }
            throw new IndexOutOfRangeException(nameof(axis) + " must be between 0 and 2");
        }

        public static int PositiveModulus(int x, int m)
        {
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        public static long PositiveModulus(long x, long m)
        {
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        public static float PositiveModulus(float x, float m)
        {
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        public static double PositiveModulus(double x, double m)
        {
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        public static decimal PositiveModulus(decimal x, decimal m)
        {
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        // Yes, OpenTK.MathHelper.Clamp exists, but only for int, float, and double.
        // Using it when not expecting it to be missing other types is dangerous.
        // Like if using long with MathHelper.Clamp, YOU'LL GET A FLOAT OF ALL THINGS!!!
        // Clamp should not be used when a preference is needed between favoring min or max when min > max.
        public static int Clamp(int n, int min, int max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static uint Clamp(uint n, uint min, uint max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static long Clamp(long n, long min, long max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static ulong Clamp(ulong n, ulong min, ulong max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static float Clamp(float n, float min, float max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static double Clamp(double n, double min, double max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static decimal Clamp(decimal n, decimal min, decimal max)
        {
            return Math.Max(Math.Min(n, max), min);
        }

        public static int RoundUpToPower(int n, int power)
        {
            Debug.Assert(power >= 2, "RoundUpToPower must have power greater than or equal to 2");
            if (n == 0)
            {
                return 0;
            }
            var value = 1;
            var nAbs = Math.Abs(n);
            while (value < nAbs) value *= power;
            return value * Math.Sign(n);
        }

        public static uint RoundUpToPower(uint n, uint power)
        {
            Debug.Assert(power >= 2, "RoundUpToPower must have power greater than or equal to 2");
            if (n == 0)
            {
                return 0;
            }
            uint value = 1;
            while (value < n) value *= power;
            return value;
        }

        public static long RoundUpToPower(long n, long power)
        {
            Debug.Assert(power >= 2, "RoundUpToPower must have power greater than or equal to 2");
            if (n == 0)
            {
                return 0;
            }
            long value = 1;
            var nAbs = Math.Abs(n);
            while (value < nAbs) value *= power;
            return value * Math.Sign(n);
        }

        public static ulong RoundUpToPower(ulong n, ulong power)
        {
            Debug.Assert(power >= 2, "RoundUpToPower must have power greater than or equal to 2");
            if (n == 0)
            {
                return 0;
            }
            ulong value = 1;
            while (value < n) value *= power;
            return value;
        }

        public static float ConvertFixed12(int value) => value / Fixed12Scalar;

        public static float ConvertFixed16(int value) => value / Fixed16Scalar;

        public static float ConvertUV(uint uvComponent) => uvComponent / UVScalar;

        public static Vector2 ConvertUV(uint u, uint v)
        {
            var scalar = UVScalar;
            return new Vector2(u / scalar, v / scalar);
        }

        public static float InterpolateValue(float src, float dst, float delta)
        {
            // Uncomment if we want clamping. Or add bool clamp as an optional parameter.
            //if (delta <= 0f) return src;
            //if (delta >= 1f) return dst;
            return (src * (1f - delta)) + (dst * (delta));
        }

        // Just use Vector3.Lerp instead.
        /*public static Vector3 InterpolateVector(Vector3 src, Vector3 dst, float delta)
        {
            // Uncomment if we want clamping. Or add bool clamp as an optional parameter.
            //if (delta <= 0f) return src;
            //if (delta >= 1f) return dst;
            //return (src * (1f - delta)) + (dst * (delta));
            return Vector3.Lerp(src, dst, delta);
        }*/

        public static Vector3 InterpolateBezierCurve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float delta)
        {
            // OpenTK's BezierCurve is only for Vector2, so we need to implement it ourselves.
            float temp;
            var r = Vector3.Zero;

            // Note: Power raised to the 0 is always 1, so we can skip those calculations.

            temp = (float)MathHelper.BinomialCoefficient(4 - 1, 0) *
                /*(float)Math.Pow(delta, 0) * */ (float)Math.Pow(1f - delta, 3);
            r += temp * p0;

            temp = (float)MathHelper.BinomialCoefficient(4 - 1, 1) *
                (float)Math.Pow(delta, 1) * (float)Math.Pow(1f - delta, 2);
            r += temp * p1;

            temp = (float)MathHelper.BinomialCoefficient(4 - 1, 2) *
                (float)Math.Pow(delta, 2) * (float)Math.Pow(1f - delta, 1);
            r += temp * p2;

            temp = (float)MathHelper.BinomialCoefficient(4 - 1, 3) *
                (float)Math.Pow(delta, 3) /* * (float)Math.Pow(1f - delta, 0)*/;
            r += temp * p3;

            return r;
        }

        public static Vector3 InterpolateBSpline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float delta)
        {
            // This is heavily simplified from other examples by removing knots and weights,
            // so there may be some mistakes here...

            const int NUM_POINTS = 4;
            const int DEGREE = 3;// NUM_POINTS - 1;
            const int start = DEGREE + 1; // Only constant when DEGREE == NUM_POINTS - 1

            delta = GeomMath.Clamp(delta, 0f, 1f); // Clamp delta
            var t = delta * (NUM_POINTS - DEGREE) + DEGREE;

            // This should always be 4 (3 + 1).
            //var start = GeomMath.Clamp((int)t, DEGREE, NUM_POINTS - 1) + 1;

            var points = new[] { p0, p1, p2, p3 };

            for (var lvl = 0; lvl < DEGREE; lvl++)
            {
                for (var i = start - DEGREE + lvl; i < start; i++)
                {
                    //var alpha = (t - i) / ((i + DEGREE - lvl) - i);
                    var alpha = (t - i) / (DEGREE - lvl); // simplified

                    var p = i - lvl;
                    points[p - 1] = Vector3.Lerp(points[p - 1], points[p], alpha);
                }
            }
            return points[start - DEGREE - 1];
        }
    }
}