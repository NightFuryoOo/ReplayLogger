using System;

namespace ReplayLogger
{
    internal static class SavedLogToast
    {
        private static string lastToastText;
        private static CustomCanvas transientCanvas;

        internal static void Record(string toastText)
        {
            if (!string.IsNullOrWhiteSpace(toastText))
            {
                lastToastText = toastText;
            }
        }

        internal static void Show(float seconds)
        {
            if (string.IsNullOrWhiteSpace(lastToastText))
            {
                return;
            }

            CustomCanvas canvas = HoGLogger.GetActiveCanvas() ?? ReplayLogger.Instance?.customCanvas;
            if (canvas != null)
            {
                canvas.ShowSavedFileToast(lastToastText, seconds);
                return;
            }

            CreateTransientCanvas();
            transientCanvas?.ShowSavedFileToast(lastToastText, seconds);
            transientCanvas?.DestroyCanvasDelayed(seconds);
        }

        private static void CreateTransientCanvas()
        {
            transientCanvas?.DestroyCanvas();
            transientCanvas = null;

            transientCanvas = new CustomCanvas(new NumberInCanvas(0), new LoadingSprite("ReplayLogger"));
            transientCanvas.ClearHud();
        }
    }
}
