using System;

namespace ProiectSPG
{
    public static class MathHelper
    {
        public static float Sinf(double a) => (float)Math.Sin(a);
        public static float Cosf(double d) => (float)Math.Cos(d);
        public static float Tanf(double a) => (float)Math.Tan(a);
        public static float Atan2f(double y, double x) => (float)Math.Atan2(y, x);
        public static float Acosf(double d) => (float)Math.Acos(d); 
    }
}
