using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace KadoExile;

public sealed class Settings : ISettings
{
	public ToggleNode Enable { get; set; } = new ToggleNode(true);

	public RangeNode<int> TravelLimit { get; set; } = new RangeNode<int>(10, 1, 100);

	public RangeNode<int> DesiredQuantity { get; set; } = new RangeNode<int>(1, 1, 60);

	public HotkeyNodeV2 StartHotkey { get; set; } = new HotkeyNodeV2(Keys.F6);

	public HotkeyNodeV2 StopHotkey { get; set; } = new HotkeyNodeV2(Keys.F7);

	[Menu("Require game highlight", "Only buy items whose ExileAPI UI element is highlighted.")]
	public ToggleNode RequireHighlight { get; set; } = new ToggleNode(true);

	public RangeNode<int> MerchantTimeoutSeconds { get; set; } = new RangeNode<int>(45, 3, 120);

	[Menu("Hideout settle delay", "Minimum delay after AreaChange before the merchant can become ready.")]
	public RangeNode<int> HideoutSettleDelayMilliseconds { get; set; } = new RangeNode<int>(1000, 250, 5000);

	public RangeNode<int> MerchantStableReads { get; set; } = new RangeNode<int>(5, 2, 10);

	public RangeNode<int> HighlightStableReads { get; set; } = new RangeNode<int>(4, 2, 10);

	[Menu("Recovery delay", "Additional memory verification delay before retrying the current merchant.")]
	public RangeNode<int> RecoveryDelayMilliseconds { get; set; } = new RangeNode<int>(750, 250, 3000);

	public RangeNode<int> ClickDelayMilliseconds { get; set; } = new RangeNode<int>(80, 20, 500);

	public RangeNode<int> VerificationDelayMilliseconds { get; set; } = new RangeNode<int>(180, 50, 1000);

	public ToggleNode DetailedLogging { get; set; } = new ToggleNode(true);

	[Menu("Completion sound", "Play treasure.wav after the configured quantity has been purchased.")]
	public ToggleNode CompletionSound { get; set; } = new ToggleNode(true);

	[Menu("Completion volume", "Completion sound volume from 0 to 100 percent.")]
	public RangeNode<int> CompletionVolume { get; set; } = new RangeNode<int>(70, 0, 100);

	[Menu("Completion sound path", "Absolute path or a file name relative to ExileAPI's Sounds directory. WAV and MP3 are supported.")]
	public TextNode CompletionSoundPath { get; set; } = new TextNode("treasure.wav");

	public ButtonNode Start { get; set; } = new ButtonNode();

	public ButtonNode Stop { get; set; } = new ButtonNode();
}
