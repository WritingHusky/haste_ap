using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Landfall.Haste;
using System.Media;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Inspired / based of the In Game Stats Mod by Qwarks

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(GraphicRaycaster))]
public class ApDebugLog : MonoBehaviour
{
    // public static LocalizedString Category = new UnlocalizedString { TableReference = "Settings", TableEntryReference = "AP Server Port" };
    public static ApDebugLog Instance { get; private set; } = null!;

    // public DisplayMode displayMode = DisplayMode.Always;
    public float yBaseOffset = 150f;
    public float xBaseOffset = -650f;
    public int fontSize = 16;
    public int lineSpacing = 20;
    public AlignmentMode alignmentMode = AlignmentMode.Left;
    public ColorizedMode colorizedMode = ColorizedMode.Colorized;
    public FontMode fontMode = FontMode.GameFont;
    public OutlineMode outlineMode = OutlineMode.Outline;
    public float _duration = 7f;
    private TMP_FontAsset _fontAsset = null!;

    private Canvas? _canvas;

    private class MessageData
    {
        public GameObject MessageObject;
        public float TimeRemaining;
        public float Height;

        public MessageData(GameObject messageObject, float timeRemaining)
        {
            MessageObject = messageObject;
            TimeRemaining = timeRemaining;
            Height = 0; // Default height, will be updated later
        }
    }

    private List<MessageData> activeMessages = new();

    private readonly object _lock = new object();

    private void Awake()
    {
        Instance = this;

        // Set up the Canvas
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1500;

        CanvasScaler canvasScaler = GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);
    }

    public void RecreateUI()
    {
        if (fontMode == FontMode.GameFont && _fontAsset == null)
        {
            _fontAsset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(font => font.name == "AkzidenzGroteskPro-Bold SDF");

            UnityMainThreadDispatcher.Instance().log(_fontAsset != null
                ? $"Font asset found: {_fontAsset.name}"
                : "Font asset not found. Using default font.");
        }

    }


    public void DisplayMessage(LogMessage message, float duration = -1f, bool isDebug = false)
    {
        string final = "";
        bool showMessage = false;
        foreach (MessagePart m in message.Parts)
        {
            if (FactSystem.GetFact(new Fact("APMessageFilter")) >= 1f)
            {
                if (m is PlayerMessagePart mp)
                {
                    if (mp.IsActivePlayer)
                    {
                        showMessage = true;
                    }
                }
                // if filter == 2f
                // TODO: figure out how to tell if this message is just a chat; probably just has one part
            }
            else
            {
                showMessage = true;
            }
            // brightens some of the darker colours without overkilling the brighter ones
            float r = m.Color.R / 255f;
            float g = m.Color.G / 255f;
            float b = m.Color.B / 255f;

            float brightness = 0.299f * r + 0.587f * g + 0.114f * b;
            float factor = 1f + (0.6f * (1f - brightness));

            r = Math.Min(r * factor, 1f);
            g = Math.Min(g * factor, 1f);
            b = Math.Min(b * factor, 1f);

            final += $"<color=#{(int)(r*255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}>{m.Text}</color> ";
        }
        if (showMessage) DisplayMessage(final, duration, isDebug);
    }

    public void DisplayMessage(string message, float duration = -1f, bool isDebug = true)
    {
        if (isDebug)
        {
            if (FactSystem.GetFact(new Fact("APDebugLogEnabled")) != 1f)
            {
                return;
            }
            message = $"<color=#FFFF00>Debug:</color> {message}";
        }

        if (duration == -1f)
        {
            duration = _duration;
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            lock (_lock)
            {
                GameObject messageObject = new GameObject("AP_Message");
                messageObject.transform.SetParent(_canvas!.transform);

                TextMeshProUGUI textComponent = messageObject.AddComponent<TextMeshProUGUI>();
                textComponent.text = message;
                textComponent.fontSize = fontSize;
                textComponent.alignment = TextAlignmentOptions.Left;
                textComponent.outlineColor = Color.black;
                textComponent.outlineWidth = 2;

                if (fontMode == FontMode.GameFont && _fontAsset != null)
                {
                    textComponent.font = _fontAsset;
                }

                RectTransform rectTransform = messageObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(600, 50); // Initial size, will adjust later
                rectTransform.anchoredPosition = new Vector2(xBaseOffset, yBaseOffset);

                // Force layout update to calculate preferred height
                Canvas.ForceUpdateCanvases();
                float messageHeight = textComponent.preferredHeight;

                activeMessages.Add(new MessageData(messageObject, duration)
                {
                    MessageObject = messageObject,
                    TimeRemaining = duration,
                    Height = messageHeight
                });
            }
        });
    }

    private void Update()
    {
        lock (_lock)
        {
            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                activeMessages[i].TimeRemaining -= Time.deltaTime;

                if (activeMessages[i].TimeRemaining <= 0)
                {
                    Destroy(activeMessages[i].MessageObject);
                    activeMessages.RemoveAt(i);
                }
            }

            float currentYOffset = yBaseOffset;
            for (int i = 0; i < activeMessages.Count; i++)
            {
                RectTransform activeRect = activeMessages[i].MessageObject.GetComponent<RectTransform>();
                activeRect.anchoredPosition = new Vector2(xBaseOffset, currentYOffset);
                currentYOffset -= activeMessages[i].Height + lineSpacing;
            }
        }
    }

}