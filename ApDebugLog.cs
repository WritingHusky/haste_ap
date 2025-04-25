using UnityEngine;
using UnityEngine.UI;
using TMPro;


/// <summary>
/// Stats display plugin object.
/// </summary>
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(GraphicRaycaster))]
public class ApDebugLog : MonoBehaviour
{
  /// <summary>
  /// Category of the plugin for settings.
  /// </summary>
  // public static LocalizedString Category = new UnlocalizedString { TableReference = "Settings", TableEntryReference = "AP Server Port" };
  /// <summary>
  /// Singleton instance of the InGameStats class.
  /// This is used to access the instance of the class from other scripts.
  /// </summary>
  public static ApDebugLog Instance { get; private set; } = null!;

  /// <summary>
  /// Indicates if the stats should be displayed only in runs.
  /// </summary>
  // public DisplayMode displayMode = DisplayMode.Always;
  /// <summary>
  /// The base Y offset for the stats text.
  /// </summary>
  public float yBaseOffset = 0f;
  /// <summary>
  /// The base Y offset for the stats text.
  /// </summary>
  public float xBaseOffset = -650f;
  /// <summary>
  /// The font size of the stats text.
  /// </summary>
  public int fontSize = 16;
  /// <summary>
  /// What alignment to use for the stats text.
  /// </summary>
  public AlignmentMode alignmentMode = AlignmentMode.Left;
  /// <summary>
  /// Indicates if the colors should be used for the stats text.
  /// </summary>
  public ColorizedMode colorizedMode = ColorizedMode.Colorized;
  /// <summary>
  /// Indicates if the game font should be used for the stats text.
  /// </summary>
  public FontMode fontMode = FontMode.GameFont;
  /// <summary>
  /// Indicates if the stats should be outlined.
  /// </summary>
  public OutlineMode outlineMode = OutlineMode.Outline;
  public float _duration = 5f;
  private TMP_FontAsset _fontAsset = null!;

  private Canvas? _canvas;

  private class MessageData
  {
    public GameObject MessageObject;
    public float TimeRemaining;

    public MessageData(GameObject messageObject, float timeRemaining)
    {
      MessageObject = messageObject;
      TimeRemaining = timeRemaining;
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

  /// <summary>
  /// Creates the empty UI elements for the enabled stats.
  /// This method is called when the mod is loaded or when the settings are changed.
  /// </summary>
  public void RecreateUI()
  {
    if (fontMode == FontMode.GameFont && _fontAsset == null)
    {
      _fontAsset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
          .FirstOrDefault(font => font.name == "AkzidenzGroteskPro-Bold SDF");

      Debug.Log(_fontAsset != null
          ? $"Font asset found: {_fontAsset.name}"
          : "Font asset not found. Using default font.");
    }

  }

  /// <summary>
  /// Displays a line of text on the canvas for a specified duration.
  /// </summary>
  /// <param name="message">The message to display.</param>
  /// <param name="duration">The duration to display the message.</param>
  public void DisplayMessage(string message, float duration = -1f)
  {
    if (duration == -1f)
    {
      duration = _duration;
    }

    // Ensure the method is executed on the main thread
    UnityMainThreadDispatcher.Instance().Enqueue(() =>
    {
      lock (_lock)
      {
        // Create a new TextMeshProUGUI object for the message
        GameObject messageObject = new GameObject("AP_Message");
        messageObject.transform.SetParent(_canvas!.transform);

        TextMeshProUGUI textComponent = messageObject.AddComponent<TextMeshProUGUI>();
        textComponent.text = message;
        textComponent.fontSize = fontSize;
        textComponent.alignment = TextAlignmentOptions.Left;
        textComponent.color = colorizedMode == ColorizedMode.Colorized ? Color.white : Color.black;

        if (fontMode == FontMode.GameFont && _fontAsset != null)
        {
          textComponent.font = _fontAsset;
        }

        // Position the message
        RectTransform rectTransform = messageObject.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = new Vector2(xBaseOffset, yBaseOffset - (activeMessages.Count * 30));
        rectTransform.sizeDelta = new Vector2(600, 50);

        activeMessages.Add(new MessageData(messageObject, duration));
      }
    });
  }

  private void Update()
  {
    lock (_lock)
    {

      // Update message timers and remove expired messages
      for (int i = activeMessages.Count - 1; i >= 0; i--)
      {
        activeMessages[i].TimeRemaining -= Time.deltaTime;

        if (activeMessages[i].TimeRemaining <= 0)
        {
          Destroy(activeMessages[i].MessageObject);
          activeMessages.RemoveAt(i);
        }
      }

      // Reposition remaining messages
      for (int i = 0; i < activeMessages.Count; i++)
      {
        RectTransform activeRect = activeMessages[i].MessageObject.GetComponent<RectTransform>();
        activeRect.anchoredPosition = new Vector2(xBaseOffset, yBaseOffset - (i * 30));
      }
    }
  }
}