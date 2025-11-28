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
        public Image flagSpriteInCanvas;

        public static Sprite flagSpriteTrue;
        public static Sprite flagSpriteFalse;


        private GameObject _canvas;

        public CustomCanvas(NumberInCanvas number, LoadingSprite loadingSprite)
        {
            numberInCanvas = number;
            this.loadingSprite = loadingSprite;

            CreateCanvas();

        }

        public void StartUpdateSprite()
        {
            _canvas?.GetComponent<MonoBehaviour>().StartCoroutine(UpdateSprite());
        }

        public void CreateCanvas()
        {
            if (_canvas != null) return;

            _canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, new Vector2(1920, 1080));

            CanvasGroup canvasGroup = _canvas.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Object.DontDestroyOnLoad(_canvas);

            prefabNumberInCanvas = CreateWatermark(_canvas, new Vector2(915f, 525f), new Vector2(85, 25));

            timeInCanvas = CreateWatermark(_canvas, new Vector2(915f, 510f), new Vector2(80, 25));

            flagSpriteInCanvas = CreateSprite(_canvas, flagSpriteTrue, new Vector2(850f, -500f), new Vector2(70, 70));


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

        public void UpdateWatermark(KeyCode keyCode)
        {
            numberInCanvas.NextGeneration(keyCode.ToString());

            prefabNumberInCanvas.text = numberInCanvas.Number.ToString();

            prefabNumberInCanvas.color = numberInCanvas.Color;
        }
        public void UpdateTime(string time)
        {
            timeInCanvas.text = time;
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
                Modding.Logger.Log("Resource not found: " + fullResourceName);
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
    }
}