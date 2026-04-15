using Modding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Policy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MonoMod.Cil.RuntimeILReferenceBag.FastDelegateInvokers;
using Object = UnityEngine.Object;

namespace ReplayLogger
{


    public class CustomCanvas
    {
        public NumberInCanvas numberInCanvas;
        public LoadingSprite loadingSprite;

        public TextMeshProUGUI prefabNumberInCanvas;
        public TextMeshProUGUI timeInCanvas;
        public TextMeshProUGUI savedFileToast;
        public TextMeshProUGUI manualStatusText;
        public Image flagSpriteInCanvas;

        public static Sprite flagSpriteTrue;
        public static Sprite flagSpriteFalse;


        private GameObject _canvas;
        private bool hudHiddenByToast;
        private bool savedNumberActive;
        private bool savedTimeActive;
        private int toastToken;
        private string lastTimeText;
        public bool HasCanvas => _canvas != null;

        public CustomCanvas(NumberInCanvas number, LoadingSprite loadingSprite)
        {
            numberInCanvas = number;
            this.loadingSprite = loadingSprite;

            CreateCanvas();

        }

        public void StartUpdateSprite()
        {
            GetCoroutineHost()?.StartCoroutine(UpdateSprite());
        }

        public void CreateCanvas()
        {
            if (_canvas != null) return;

            _canvas = new GameObject("ReplayLoggerCanvas");
            Canvas canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvas.AddComponent<GraphicRaycaster>();
            CanvasGroup canvasGroup = _canvas.AddComponent<CanvasGroup>();
            _canvas.AddComponent<CanvasCoroutineHost>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Object.DontDestroyOnLoad(_canvas);

            prefabNumberInCanvas = CreateWatermark(_canvas, new Vector2(915f, 525f), new Vector2(85, 25));

            timeInCanvas = CreateWatermark(_canvas, new Vector2(915f, 510f), new Vector2(80, 25));

            flagSpriteInCanvas = CreateSprite(_canvas, flagSpriteTrue, new Vector2(850f, -500f), new Vector2(70, 70));

            savedFileToast = CreateWatermarkTopRight(_canvas, new Vector2(-10f, -10f), new Vector2(520f, 50f));
            savedFileToast.gameObject.SetActive(false);

            manualStatusText = CreateWatermarkTopRight(_canvas, new Vector2(-10f, -60f), new Vector2(520f, 40f));
            manualStatusText.text = "Currently Not Logging";
            manualStatusText.color = Color.red;
            manualStatusText.gameObject.SetActive(false);

            _canvas.SetActive(true);
            flagSpriteInCanvas.gameObject.SetActive(false);
        }

        public void DestroyCanvas()
        {
            if (_canvas != null) Object.Destroy(_canvas);
            _canvas = null;

            if (prefabNumberInCanvas != null) Object.Destroy(prefabNumberInCanvas);
            prefabNumberInCanvas = null;

            if (timeInCanvas != null) Object.Destroy(timeInCanvas);
            timeInCanvas = null;

            if (flagSpriteInCanvas != null) Object.Destroy(flagSpriteInCanvas);
            flagSpriteInCanvas = null;

            if (savedFileToast != null) Object.Destroy(savedFileToast);
            savedFileToast = null;

            if (manualStatusText != null) Object.Destroy(manualStatusText);
            manualStatusText = null;
        }

        public void DestroyCanvasDelayed(float seconds)
        {
            if (_canvas == null)
            {
                DestroyCanvas();
                return;
            }

            GetCoroutineHost()?.StartCoroutine(DestroyAfterDelay(seconds));
        }

        private Image CreateSprite(GameObject canvas, Sprite sprite, Vector2 pos, Vector2 size)
        {
            GameObject imageObject = new GameObject("LoadingSprite");
            imageObject.transform.SetParent(canvas.transform, false);

            Image imageComponent = imageObject.AddComponent<Image>();
            imageComponent.sprite = sprite;
            imageComponent.color *= new Color(1, 1, 1, 0.4f);

            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            rectTransform.anchoredPosition = pos;
            rectTransform.sizeDelta = size;

            imageObject.SetActive(true);

            return imageComponent;
        }

        private TextMeshProUGUI CreateWatermark(GameObject canvas, Vector2 pos, Vector2 size)
        {

            GameObject watermarkObject = new GameObject("Watermark");
            watermarkObject.transform.SetParent(canvas.transform, false);

            TextMeshProUGUI textMeshProComponent = watermarkObject.AddComponent<TextMeshProUGUI>();
            textMeshProComponent.autoSizeTextContainer = true;
            textMeshProComponent.enableAutoSizing = true;

            textMeshProComponent.color = Color.green*new Color(1,1,1,0.5f);

            RectTransform rectTransform = watermarkObject.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = size;

            rectTransform.localPosition = pos;


            watermarkObject.SetActive(true);

            return textMeshProComponent;
        }

        private TextMeshProUGUI CreateWatermarkTopRight(GameObject canvas, Vector2 offset, Vector2 size)
        {
            GameObject toastObject = new GameObject("SavedFileToast");
            toastObject.transform.SetParent(canvas.transform, false);

            TextMeshProUGUI text = toastObject.AddComponent<TextMeshProUGUI>();
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 20f;
            text.color = Color.green;
            text.alignment = TextAlignmentOptions.TopRight;
            text.enableWordWrapping = false;

            RectTransform rect = toastObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;

            toastObject.SetActive(true);

            return text;
        }

        public void UpdateWatermark(KeyCode keyCode)
        {
            if (numberInCanvas == null || prefabNumberInCanvas == null)
            {
                return;
            }

            numberInCanvas.NextGeneration(keyCode);
            prefabNumberInCanvas.text = numberInCanvas.Number.ToString();
            prefabNumberInCanvas.color = numberInCanvas.Color;
        }
        public void UpdateTime(string time)
        {
            if (timeInCanvas == null)
            {
                return;
            }

            string value = time ?? string.Empty;
            if (string.Equals(lastTimeText, value, StringComparison.Ordinal))
            {
                return;
            }

            lastTimeText = value;
            timeInCanvas.text = value;
        }

        public static Sprite LoadEmbeddedSprite(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string fullResourceName = assembly.GetName().Name + "." + "Resources" + "." + resourceName;

            Stream stream = assembly.GetManifestResourceStream(fullResourceName);

            if (stream != null)
            {
                byte[] imageData = new byte[stream.Length];
                stream.Read(imageData, 0, (int)stream.Length);

                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);

                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                return sprite;
            }
            else
            {
                global::ReplayLogger.InternalDiagnostics.Info("Resource not found: " + fullResourceName);
                return null;
            }
        }
        public IEnumerator UpdateSprite()
        {

            flagSpriteInCanvas.sprite = loadingSprite.Flag ? flagSpriteTrue : flagSpriteFalse;
            flagSpriteInCanvas.gameObject.SetActive(true);
            loadingSprite.NextGeneration();

            yield return new WaitForSecondsRealtime(loadingSprite.SecondCount / 2);

            flagSpriteInCanvas.sprite = loadingSprite.Flag ? flagSpriteTrue : flagSpriteFalse;

            yield return new WaitForSecondsRealtime(loadingSprite.SecondCount / 2);

            flagSpriteInCanvas.gameObject.SetActive(false);

        }

        public void ShowSavedFileToast(string fileName, float seconds)
        {
            if (_canvas == null || savedFileToast == null)
            {
                return;
            }

            toastToken++;
            int token = toastToken;

            if (!hudHiddenByToast)
            {
                savedNumberActive = prefabNumberInCanvas != null && prefabNumberInCanvas.gameObject.activeSelf;
                savedTimeActive = timeInCanvas != null && timeInCanvas.gameObject.activeSelf;
                hudHiddenByToast = true;
            }

            if (prefabNumberInCanvas != null)
            {
                prefabNumberInCanvas.gameObject.SetActive(false);
            }

            if (timeInCanvas != null)
            {
                timeInCanvas.gameObject.SetActive(false);
            }

            savedFileToast.text = fileName;
            savedFileToast.gameObject.SetActive(true);
            GetCoroutineHost()?.StartCoroutine(HideToastAfterDelay(seconds, token));
        }

        public void ShowManualStatus(bool show)
        {
            if (_canvas == null || manualStatusText == null)
            {
                return;
            }

            manualStatusText.gameObject.SetActive(show);
        }

        private IEnumerator HideToastAfterDelay(float seconds, int token)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (token != toastToken)
            {
                yield break;
            }

            if (savedFileToast != null)
            {
                savedFileToast.gameObject.SetActive(false);
            }

            if (hudHiddenByToast)
            {
                if (prefabNumberInCanvas != null)
                {
                    prefabNumberInCanvas.gameObject.SetActive(savedNumberActive);
                }

                if (timeInCanvas != null)
                {
                    timeInCanvas.gameObject.SetActive(savedTimeActive);
                }

                hudHiddenByToast = false;
            }
        }

        public void ClearHud()
        {
            if (prefabNumberInCanvas != null)
            {
                prefabNumberInCanvas.gameObject.SetActive(false);
            }

            if (timeInCanvas != null)
            {
                timeInCanvas.gameObject.SetActive(false);
            }

            if (flagSpriteInCanvas != null)
            {
                flagSpriteInCanvas.gameObject.SetActive(false);
            }
        }

        private IEnumerator DestroyAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            DestroyCanvas();
        }

        private CanvasCoroutineHost GetCoroutineHost()
        {
            return _canvas != null ? _canvas.GetComponent<CanvasCoroutineHost>() : null;
        }
    }

    internal sealed class CanvasCoroutineHost : MonoBehaviour
    {
    }
}



