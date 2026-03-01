using UnityEngine;

namespace Extensions
{
    public static class GameObjectExtensions
    {
        public static void ClearChildren(this GameObject parent)
        {
            for (var i = parent.transform.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(parent.transform.GetChild(i).gameObject);
            }
        }
    }
}