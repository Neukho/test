namespace UnityEngine { [System.Flags] public enum PenStatus { None = 0, Contact = 1, Barrel = 2, Inverted = 4, Eraser = 8 } }
public static class StringCompatShim { public static bool Contains(this string s, char c) { return s.IndexOf(c) >= 0; } }
