using UnityEngine;

namespace ReplayLogger
{

    public static class GameObjectExtensions
    {
        public static string GetFullPath(this GameObject gameObject)
        {
            Transform currentTransform = gameObject.transform;
            string path = currentTransform.name;

            while (currentTransform.parent != null)
            {
                currentTransform = currentTransform.parent;
                path = currentTransform.name + "/" + path;
            }

            return path;
        }
    }
}
