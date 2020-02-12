﻿namespace PSXPrev.Classes
{
    public class Color
    {
        public static readonly Color Red = new Color(1f, 0f, 0f);
        public static readonly Color Green = new Color(0f, 1f, 0f);
        public static readonly Color Blue = new Color(0f, 0f, 1f);
        public static readonly Color White = new Color(1f, 1f, 1f);
        public static readonly Color Grey = new Color(0.5f, 0.5f, 0.5f);

        public float R;
        public float G;
        public float B;

        public Color(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public override string ToString()
        {
            return R + "|" + G + "|" + B;
        }
    }
}