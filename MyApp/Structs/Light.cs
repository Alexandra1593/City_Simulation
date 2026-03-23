using SharpDX;
using System.Linq;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Light
    {
        public const int MaxLights = 16;

        public Vector3 Strength;
        public float FalloffStart;  // Point/spot light only.
        public Vector3 Direction;   // Directional/spot light only.
        public float FalloffEnd;    // Point/spot light only.
        public Vector3 Position;    // Point/spot light only.
        public float SpotPower;     // Spot light only.

        public static Light Default => new Light
        {
            Strength = new Vector3(0.5f),
            FalloffStart = 1.0f,
            Direction = -Vector3.UnitY,
            FalloffEnd = 10.0f,
            Position = Vector3.Zero,
            SpotPower = 64.0f
        };

        public static Light[] DefaultArray => Enumerable.Repeat(Default, MaxLights).ToArray();
    }
}
