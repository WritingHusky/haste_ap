using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipelago.MultiClient.Net.Colors;
using Landfall.Haste;
using System.Media;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Runtime.CompilerServices;

// Inspired / based of the In Game Stats Mod by Qwarks

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(GraphicRaycaster))]
public class ApDebugLog : MonoBehaviour
{
    // public static LocalizedString Category = new UnlocalizedString { TableReference = "Settings", TableEntryReference = "AP Server Port" };
    public static ApDebugLog Instance { get; private set; } = null!;

    // public DisplayMode displayMode = DisplayMode.Always;

    // initial values for 1080p screen, will be recalculated almost immediately
    public float yBaseOffset = -150f;
    public float xBaseOffset = 20f;
    public bool messagesGoDown = true;
    public Vector2 ancMin = Vector2.zero;
    public Vector2 ancMax = Vector2.zero;
    public Vector2 pivot = Vector2.zero;
    
    public int fontSize = 16;
    public int lineSpacing = 20;

    // easier to just store the custom setting seperately and only call them when set to custom
    public float yCustomOffset = -150f;
    public float xCustomOffset = 20f;
    public bool customMessagesGoDown = true;
    public TextAlignmentOptions customAlignmentMode = TextAlignmentOptions.Left;

    public APLogLocation windowPosition = APLogLocation.TopRight;

    public TextAlignmentOptions alignmentMode = TextAlignmentOptions.Left;
    public ColorizedMode colorizedMode = ColorizedMode.Colorized;
    public FontMode fontMode = FontMode.GameFont;
    public OutlineMode outlineMode = OutlineMode.Outline;
    public float _duration = 7f;
    private TMP_FontAsset _fontAsset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(font => font.name == "AkzidenzGroteskPro-Bold SDF");

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

        RecalculatePosition();

        CanvasScaler canvasScaler = GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);
    }

    public void BuildFont()
    {
        if (fontMode == FontMode.GameFont && _fontAsset == null)
        {
            _fontAsset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(font => font.name == "AkzidenzGroteskPro-Bold SDF");

            UnityMainThreadDispatcher.Instance().logError(_fontAsset != null
                ? $"Font asset found: {_fontAsset.name}"
                : "Font asset not found. Using default font.");

        }
    }

    public void RecalculatePosition()
    {
        switch (windowPosition)
        {
            case APLogLocation.TopRight:
                yBaseOffset = -150f;
                xBaseOffset = 20f;
                ancMin = new Vector2(0, 1);
                ancMax = new Vector2(0, 1);
                pivot = new Vector2(0, 1);
                alignmentMode = TextAlignmentOptions.Left;
                messagesGoDown = true;
                break;
            case APLogLocation.MidRight:
                yBaseOffset = -Screen.height / 3;
                xBaseOffset = 20f;
                ancMin = new Vector2(0, 1);
                ancMax = new Vector2(0, 1);
                pivot = new Vector2(0, 1);
                alignmentMode = TextAlignmentOptions.Left;
                messagesGoDown = true;
                break;
            case APLogLocation.BottomRight:
                yBaseOffset = -Screen.height + 50f;
                xBaseOffset = 20f;
                ancMin = new Vector2(0, 1);
                ancMax = new Vector2(0, 1);
                pivot = new Vector2(0, 1);
                alignmentMode = TextAlignmentOptions.Left;
                messagesGoDown = false;
                break;
            case APLogLocation.TopLeft:
                yBaseOffset = -150f;
                xBaseOffset = -20f;
                ancMin = new Vector2(1, 1);
                ancMax = new Vector2(1, 1);
                pivot = new Vector2(1, 1);
                alignmentMode = TextAlignmentOptions.Right;
                messagesGoDown = true;
                break;
            case APLogLocation.MidLeft:
                yBaseOffset = -Screen.height / 3;
                xBaseOffset = -20f;
                ancMin = new Vector2(1, 1);
                ancMax = new Vector2(1, 1);
                pivot = new Vector2(1, 1);
                alignmentMode = TextAlignmentOptions.Right;
                messagesGoDown = true;
                break;
            case APLogLocation.BottomLeft:
                yBaseOffset = -Screen.height + 50f;
                xBaseOffset = -20f;
                ancMin = new Vector2(1, 1);
                ancMax = new Vector2(1, 1);
                pivot = new Vector2(1, 1);
                alignmentMode = TextAlignmentOptions.Right;
                messagesGoDown = false;
                break;
            case APLogLocation.Custom:
                yBaseOffset = yCustomOffset;
                xBaseOffset = xCustomOffset;
                alignmentMode = customAlignmentMode;
                if (alignmentMode == TextAlignmentOptions.Left)
                {
                    ancMin = new Vector2(0, 1);
                    ancMax = new Vector2(0, 1);
                    pivot = new Vector2(0, 1);
                } else
                {
                    ancMin = new Vector2(1, 1);
                    ancMax = new Vector2(1, 1);
                    pivot = new Vector2(1, 1);
                }
                messagesGoDown = customMessagesGoDown;
                break;
        }
        DisplayMessage($"Log Position X: {xBaseOffset}");
        DisplayMessage($"Log Position Y: {yBaseOffset}");
    }

    public void DisplayMessage(LogMessage message, float duration = -1f, bool isDebug = false)
    {
        string final = "";
        int mpcount = 0;
        bool showMessage = false;
        foreach (MessagePart m in message.Parts)
        {
            mpcount++;
            if (FactSystem.GetFact(new Fact("APMessageFilter")) >= 1f)
            {
                if (m is PlayerMessagePart mp)
                {
                    if (mp.IsActivePlayer)
                    {
                        showMessage = true;
                    }
                }
            }
            else
            {
                showMessage = true;
            }
            float r = 0f;
            float g = 0f;
            float b = 0f;
            if (m.PaletteColor == PaletteColor.Plum)
            {
                // fix the progressive item colour cuz its shit
                r = 175f / 255f;
                g = 153f / 255f;
                b = 239f / 255f;
            } else if (m.PaletteColor == PaletteColor.SlateBlue)
            {
                // fix the useful item colour cuz its shit
                r = 109f / 255f;
                g = 139f / 255f;
                b = 232f / 255f;
            }
            else
            {
                // brightens some of the darker colours without overkilling the brighter ones
                r = m.Color.R / 255f;
                g = m.Color.G / 255f;
                b = m.Color.B / 255f;

                float brightness = 0.299f * r + 0.587f * g + 0.114f * b;
                float factor = 1f + (0.6f * (1f - brightness));

                r = Math.Min(r * factor, 1f);
                g = Math.Min(g * factor, 1f);
                b = Math.Min(b * factor, 1f);


            }
            final += $"<color=#{(int)(r*255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}>{m.Text}</color>";
        }
        if (FactSystem.GetFact(new Fact("APMessageFilter")) >= 2f && mpcount == 1)
        {
            showMessage = true;
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
                textComponent.alignment = alignmentMode;

                if (fontMode == FontMode.GameFont && _fontAsset != null)
                {
                    textComponent.font = _fontAsset;
                    textComponent.fontSharedMaterial = _fontAsset.material;
                }

                textComponent.outlineColor = Color.black;
                textComponent.outlineWidth = 0.5f;

                RectTransform rectTransform = messageObject.GetComponent<RectTransform>();
                rectTransform.anchorMin = ancMin;
                rectTransform.anchorMax = ancMax;
                rectTransform.pivot = pivot;
                rectTransform.sizeDelta = new Vector2(Screen.width / 3f, fontSize + 5f);
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
                currentYOffset += (activeMessages[i].Height + lineSpacing) * (messagesGoDown ? -1 : 1);
            }
        }
    }

}