using UnityEngine;

namespace Sza.Bow
{
    public static class MagicBowCollision
    {
        public static bool RayCapsule(Vector3 origin, Vector3 direction, Vector3 bottom, Vector3 top,
            float radius, out float distance)
        {
            Vector3 axis = top - bottom, offset = origin - bottom;
            float length2 = axis.sqrMagnitude;
            Vector3 closest = bottom + axis * (length2 > 0.000001f ? Mathf.Clamp01(Vector3.Dot(offset, axis) / length2) : 0f);
            if ((origin - closest).sqrMagnitude <= radius * radius) { distance = 0f; return true; }
            float along = Vector3.Dot(axis, direction), height = Vector3.Dot(axis, offset);
            float b = length2 * Vector3.Dot(direction, offset) - height * along;
            float a = length2 - along * along;
            float c = length2 * offset.sqrMagnitude - height * height - radius * radius * length2;
            float discriminant = b * b - a * c;
            distance = float.PositiveInfinity;
            if (a > 0.000001f && discriminant >= 0f)
            {
                float t = (-b - Mathf.Sqrt(discriminant)) / a;
                float y = height + t * along;
                if (t >= 0f && y >= 0f && y <= length2) distance = t;
            }
            distance = Mathf.Min(distance, Sphere(origin, direction, bottom, radius), Sphere(origin, direction, top, radius));
            return !float.IsPositiveInfinity(distance);
        }
        private static float Sphere(Vector3 origin, Vector3 direction, Vector3 center, float radius)
        {
            Vector3 offset = origin - center;
            float b = Vector3.Dot(offset, direction), h = b * b - offset.sqrMagnitude + radius * radius;
            if (h < 0f) return float.PositiveInfinity;
            float t = -b - Mathf.Sqrt(h);
            return t >= 0f ? t : float.PositiveInfinity;
        }
    }
}
