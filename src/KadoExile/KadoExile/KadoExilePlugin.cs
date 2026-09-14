using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using KadoExile.Core;
using KadoExile.Models;
using KadoExile.Services;
using SharpDX;

namespace KadoExile;

public sealed class KadoExilePlugin : BaseSettingsPlugin<Settings>
{
	private sealed record AreaTransition(long Generation, string Name, DateTimeOffset ChangedAt);

	private enum ProfileEditorMode
	{
		Viewing,
		Creating
	}

	private MerchantSnapshot _snapshot = MerchantSnapshot.Closed;

	private MerchantMemoryReader? _reader;

	private MerchantPurchaseService? _purchase;

	private QuickMoveService? _quickMove;

	private TradeAutomationService? _tradeAutomation;

	private PoeSessionProfileStore? _sessionProfiles;

	private TradeSearchProfileStore? _tradeProfiles;

	private CompletionSoundPlayer? _completionSoundPlayer;

	private KadoLogger? _log;

	private CancellationTokenSource? _operationCancellation;

	private Task? _operation;

	private MerchantWindowKind _lastLoggedWindow;

	private long _areaGeneration;

	private AreaTransition _areaTransition = new AreaTransition(0L, "Unknown", DateTimeOffset.MinValue);

	private string _sessionProfileNameBuffer = "Default";

	private string _tradeProfileNameBuffer = "Default";

	private string _poeSessionBuffer = string.Empty;

	private string _tradeUrlBuffer = string.Empty;

	private string _tradeRegexBuffer = string.Empty;

	private string _profileStatus = string.Empty;

	private ProfileEditorMode _sessionEditorMode;

	private ProfileEditorMode _tradeEditorMode;

	private bool OperationIsActive
	{
		get
		{
			Task operation = _operation;
			if (operation != null)
			{
				return !operation.IsCompleted;
			}
			return false;
		}
	}

	public override bool Initialise()
	{
		base.Name = "Kado-Exile";
		_log = new KadoLogger(base.DirectoryFullName, (Action<string>)base.LogMessage)
		{
			Detailed = base.Settings.DetailedLogging.Value
		};
		InitialiseProfiles();
		_reader = new MerchantMemoryReader(base.GameController);
		_completionSoundPlayer = new CompletionSoundPlayer(FindSoundsDirectory());
		InputController input = new InputController();
		_purchase = new MerchantPurchaseService(() => Volatile.Read(in _snapshot), GetWindowOffset, input, _log);
		_quickMove = new QuickMoveService(() => Volatile.Read(in _snapshot), GetWindowOffset, input, _log);
		_tradeAutomation = new TradeAutomationService(() => Volatile.Read(in _snapshot), CaptureAreaReadiness, _purchase, input, _log);
		ButtonNode start = base.Settings.Start;
		start.OnPressed = (Action)Delegate.Combine(start.OnPressed, new Action(StartTradeAutomation));
		ButtonNode stop = base.Settings.Stop;
		stop.OnPressed = (Action)Delegate.Combine(stop.OnPressed, new Action(StopOperation));
		_log.Info("START", "version=0.6.0, memory source=ExileAPI");
		return true;
	}

	public override void DrawSettings()
	{
		base.DrawSettings();
		DrawProfileManager();
	}

	public override void Render()
	{
		if (!base.Settings.Enable.Value || _reader == null)
		{
			return;
		}
		try
		{
			MerchantSnapshot merchantSnapshot = _reader.Capture();
			Volatile.Write(ref _snapshot, merchantSnapshot);
			_log.Detailed = base.Settings.DetailedLogging.Value;
			if (merchantSnapshot.Window != _lastLoggedWindow)
			{
				_log.Info("WINDOW", merchantSnapshot.IsOpen ? $"opened={merchantSnapshot.Window}, address=0x{merchantSnapshot.WindowAddress:X}, items={merchantSnapshot.Items.Count}, rawHighlights={merchantSnapshot.Items.Count((MerchantItemSnapshot item) => item.RawHighlight)}, saturated={merchantSnapshot.Items.Count((MerchantItemSnapshot item) => item.IsSaturated)}, effectiveHighlights={merchantSnapshot.Items.Count((MerchantItemSnapshot item) => item.IsHighlighted)}" : $"closed previous={_lastLoggedWindow}");
				if (merchantSnapshot.IsOpen && merchantSnapshot.Items.Count == 0)
				{
					_log.Info("MERCHANT_MEMORY_UNAVAILABLE", $"window={merchantSnapshot.Window}, address=0x{merchantSnapshot.WindowAddress:X}, items=0; waiting without clicking");
				}
				_lastLoggedWindow = merchantSnapshot.Window;
			}
		}
		catch (Exception ex)
		{
			_log?.Info("MEMORY_ERROR", ex.Message);
		}
		if (base.Settings.StartHotkey.PressedOnce())
		{
			StartTradeAutomation();
		}
		if (base.Settings.StopHotkey.PressedOnce())
		{
			StopOperation();
		}
	}

	public override void AreaChange(AreaInstance area)
	{
		long generation = Interlocked.Increment(ref _areaGeneration);
		Volatile.Write(ref _areaTransition, new AreaTransition(generation, area.Name, DateTimeOffset.UtcNow));
		Volatile.Write(ref _snapshot, MerchantSnapshot.Closed);
		_lastLoggedWindow = MerchantWindowKind.None;
		_log?.Info("AREA", "changed=" + area.Name);
	}

	public override void OnUnload()
	{
		StopOperation();
		_completionSoundPlayer?.Dispose();
		base.OnUnload();
	}

	private void StartTradeAutomation()
	{
		if (_tradeAutomation == null || _sessionProfiles == null || _tradeProfiles == null || IsOperationRunning())
		{
			return;
		}
		if (_sessionEditorMode == ProfileEditorMode.Creating || _tradeEditorMode == ProfileEditorMode.Creating)
		{
			_profileStatus = "Finish or cancel the new profile before starting automation.";
			_log?.Info("PROFILE_INVALID", "reason=profile creation is in progress");
			return;
		}
		PoeSessionProfile sessionProfile = _sessionProfiles.ActiveProfile.Snapshot();
		TradeSearchProfile tradeProfile = _tradeProfiles.ActiveProfile.Snapshot();
		if (!ProfileValidator.TryValidate(new TradeProfile(tradeProfile.Id, tradeProfile.Name, sessionProfile.PoeSessionId, tradeProfile.TradeUrl, tradeProfile.ItemRegex, tradeProfile.CreatedAt, tradeProfile.UpdatedAt), out string error))
		{
			_profileStatus = error;
			_log?.Info("PROFILE_INVALID", $"session=\"{sessionProfile.Name}\", trade=\"{tradeProfile.Name}\", reason={error}");
			return;
		}
		int desiredQuantity = base.Settings.DesiredQuantity.Value;
		_log?.Info("AUTOMATION_START", $"session=\"{sessionProfile.Name}\", trade=\"{tradeProfile.Name}\"");
		StartOperation(async delegate(CancellationToken cancellationToken)
		{
			int num = await _tradeAutomation.RunAsync(tradeProfile.TradeUrl, sessionProfile.PoeSessionId, base.Settings.TravelLimit.Value, tradeProfile.ItemRegex, desiredQuantity, base.Settings.RequireHighlight.Value, TimeSpan.FromSeconds(base.Settings.MerchantTimeoutSeconds.Value), base.Settings.HideoutSettleDelayMilliseconds.Value, base.Settings.MerchantStableReads.Value, base.Settings.HighlightStableReads.Value, base.Settings.RecoveryDelayMilliseconds.Value, base.Settings.ClickDelayMilliseconds.Value, base.Settings.VerificationDelayMilliseconds.Value, cancellationToken);
			_log.Info("AUTOMATION_DONE", $"session=\"{sessionProfile.Name}\", trade=\"{tradeProfile.Name}\", purchased={num}/{desiredQuantity}");
			if (num >= desiredQuantity)
			{
				PlayCompletionSound();
			}
		});
	}

	private void StartOperation(Func<CancellationToken, Task> operation)
	{
		_operationCancellation?.Dispose();
		_operationCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _operationCancellation.Token;
		_operation = Task.Run(async delegate
		{
			try
			{
				await operation(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				_log?.Info("STOP", "operation cancelled");
			}
			catch (Exception ex2)
			{
				_log?.Info("ERROR", ex2.ToString());
			}
		}, cancellationToken);
	}

	private bool IsOperationRunning()
	{
		Task operation = _operation;
		if (operation != null && !operation.IsCompleted)
		{
			_log?.Info("BUSY", "an operation is already running");
			return true;
		}
		return false;
	}

	private void StopOperation()
	{
		_operationCancellation?.Cancel();
	}

	private SharpDX.Vector2 GetWindowOffset()
	{
		return base.GameController.Window.GetWindowRectangleTimeCache.TopLeft;
	}

	private AreaReadinessSnapshot CaptureAreaReadiness()
	{
		AreaTransition areaTransition = Volatile.Read(in _areaTransition);
		long generation = areaTransition.Generation;
		string name = areaTransition.Name;
		DateTimeOffset changedAt = areaTransition.ChangedAt;
		bool inGame = base.GameController.InGame;
		bool isLoading = base.GameController.IsLoading;
		Entity player = base.GameController.Player;
		return new AreaReadinessSnapshot(generation, name, changedAt, inGame, isLoading, player != null && player.IsValid);
	}

	private void PlayCompletionSound()
	{
		if (!base.Settings.CompletionSound.Value || base.Settings.CompletionVolume.Value <= 0)
		{
			_log?.Info("SOUND", "completion sound disabled");
			return;
		}
		try
		{
			SoundPlaybackResult soundPlaybackResult = _completionSoundPlayer.Play(base.Settings.CompletionSoundPath.Value, base.Settings.CompletionVolume.Value);
			if (soundPlaybackResult.Success)
			{
				_log?.Info("SOUND", $"backend=WinMM, file=\"{soundPlaybackResult.Path}\", volume={soundPlaybackResult.VolumePercent}%, result={soundPlaybackResult.Message}");
			}
			else
			{
				_log?.Info("SOUND_WARNING", $"backend=WinMM, file=\"{soundPlaybackResult.Path}\", reason={soundPlaybackResult.Message}; trying ExileAPI fallback");
				base.GameController.SoundController.PlaySound(Path.GetFileName(soundPlaybackResult.Path), (float)soundPlaybackResult.VolumePercent / 100f);
				_log?.Info("SOUND", $"backend=ExileAPI-fallback, file=\"{Path.GetFileName(soundPlaybackResult.Path)}\", volume={soundPlaybackResult.VolumePercent}%");
			}
		}
		catch (Exception ex)
		{
			_log?.Info("SOUND_ERROR", "playback failed: " + ex.Message);
		}
	}

	private string FindSoundsDirectory()
	{
		for (DirectoryInfo directoryInfo = new DirectoryInfo(base.DirectoryFullName); directoryInfo != null; directoryInfo = directoryInfo.Parent)
		{
			string text = Path.Combine(directoryInfo.FullName, "Sounds");
			if (Directory.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(base.DirectoryFullName, "Sounds");
	}

	private void InitialiseProfiles()
	{
		try
		{
			string configDirectory = Path.Combine(base.ConfigDirectory, "Kado-Exile");
			ProfileStore profileStore = new ProfileStore(configDirectory, new DpapiSecretProtector());
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			TradeProfile legacyProfile = new TradeProfile(Guid.NewGuid(), "Default", string.Empty, "https://www.pathofexile.com/trade", string.Empty, utcNow, utcNow);
			profileStore.LoadOrMigrate(legacyProfile);
			_sessionProfiles = new PoeSessionProfileStore(configDirectory, new DpapiSecretProtector());
			_tradeProfiles = new TradeSearchProfileStore(configDirectory);
			_sessionProfiles.LoadOrSeed(profileStore.Profiles, profileStore.ActiveProfileId);
			_tradeProfiles.LoadOrSeed(profileStore.Profiles, profileStore.ActiveProfileId);
			ApplySessionProfile(_sessionProfiles.ActiveProfile, logSelection: false);
			ApplyTradeProfile(_tradeProfiles.ActiveProfile, logSelection: false);
			bool value = LegacySettingsScrubber.ScrubPoeSessionId(Path.Combine(base.ConfigDirectory, "KadoExile_settings.json"));
			_log?.Info("PROFILE_LOAD", $"session=\"{_sessionProfiles.ActiveProfile.Name}\", trade=\"{_tradeProfiles.ActiveProfile.Name}\", sessionCount={_sessionProfiles.Profiles.Count}, tradeCount={_tradeProfiles.Profiles.Count}, legacySecretRemoved={value}");
			if (_sessionProfiles.ProfilesRequiringPoeSessionReentry > 0)
			{
				_profileStatus = "Enter POESESSID in the selected session profile and save it.";
				_log?.Info("PROFILE_RECOVERY", $"reason=DPAPI_UNAVAILABLE, profiles={_sessionProfiles.ProfilesRequiringPoeSessionReentry}, action=POESESSID_REENTRY_REQUIRED");
			}
		}
		catch (Exception ex)
		{
			_profileStatus = ex.Message;
			_log?.Info("PROFILE_ERROR", ex.Message);
			_sessionProfiles = null;
			_tradeProfiles = null;
		}
	}

	private void DrawProfileManager()
	{
		if (_sessionProfiles == null || _tradeProfiles == null)
		{
			ImGui.TextWrapped("Profile Manager unavailable: " + _profileStatus);
			return;
		}
		ImGui.SeparatorText("POESESSID Profiles");
		DrawSessionProfiles(_sessionProfiles);
		ImGui.SeparatorText("Trade Profiles");
		DrawTradeProfiles(_tradeProfiles);
		if (OperationIsActive)
		{
			ImGui.TextWrapped("Profile switching is locked until the current operation stops.");
		}
		if (!string.IsNullOrWhiteSpace(_profileStatus))
		{
			ImGui.TextWrapped(_profileStatus);
		}
	}

	private void DrawSessionProfiles(PoeSessionProfileStore store)
	{
		PoeSessionProfile active = store.ActiveProfile;
		bool flag = _sessionEditorMode == ProfileEditorMode.Creating;
		ImGui.BeginDisabled(OperationIsActive || flag);
		if (ImGui.BeginCombo("Active POESESSID profile##session_active", active.Name))
		{
			foreach (PoeSessionProfile profile in store.Profiles)
			{
				bool flag2 = profile.Id == active.Id;
				if (ImGui.Selectable(profile.Name, flag2))
				{
					ApplySessionProfile(store.Select(profile.Id), logSelection: true);
				}
				if (flag2)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();
		ImGui.BeginDisabled(OperationIsActive);
		ImGui.InputText("Session profile name##session_name", ref _sessionProfileNameBuffer, 128u);
		ImGui.InputText("POESESSID##session_secret", ref _poeSessionBuffer, 512u, (ImGuiInputTextFlags)32768);
		if (flag)
		{
			if (ImGui.Button("Create session profile##session_create"))
			{
				RunProfileAction(delegate
				{
					if (string.IsNullOrWhiteSpace(_poeSessionBuffer))
					{
						throw new InvalidOperationException("POESESSID is empty.");
					}
					PoeSessionProfile poeSessionProfile = store.Create(_sessionProfileNameBuffer, _poeSessionBuffer);
					ApplySessionProfile(poeSessionProfile, logSelection: false);
					_log?.Info("PROFILE_CREATE", $"type=session, name=\"{poeSessionProfile.Name}\", count={store.Profiles.Count}");
				});
			}
			if (ImGui.Button("Cancel session profile##session_cancel"))
			{
				ApplySessionProfile(store.ActiveProfile, logSelection: false);
			}
		}
		else
		{
			if (ImGui.Button("New session profile##session_new"))
			{
				BeginSessionCreation(store);
			}
			if (ImGui.Button("Save session profile##session_save"))
			{
				RunProfileAction(delegate
				{
					PoeSessionProfile poeSessionProfile = store.Update(active.Id, _sessionProfileNameBuffer, _poeSessionBuffer);
					ApplySessionProfile(poeSessionProfile, logSelection: false);
					_log?.Info("PROFILE_SAVE", "type=session, name=\"" + poeSessionProfile.Name + "\"");
				});
			}
		}
		ImGui.EndDisabled();
	}

	private void DrawTradeProfiles(TradeSearchProfileStore store)
	{
		TradeSearchProfile active = store.ActiveProfile;
		bool flag = _tradeEditorMode == ProfileEditorMode.Creating;
		ImGui.BeginDisabled(OperationIsActive || flag);
		if (ImGui.BeginCombo("Active Trade profile##trade_active", active.Name))
		{
			foreach (TradeSearchProfile profile in store.Profiles)
			{
				bool flag2 = profile.Id == active.Id;
				if (ImGui.Selectable(profile.Name, flag2))
				{
					ApplyTradeProfile(store.Select(profile.Id), logSelection: true);
				}
				if (flag2)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();
		ImGui.BeginDisabled(OperationIsActive);
		ImGui.InputText("Trade profile name##trade_name", ref _tradeProfileNameBuffer, 128u);
		ImGui.InputText("Trade URL##trade_url", ref _tradeUrlBuffer, TradeUrlValidator.MaximumLength + 2);
		ImGui.InputTextMultiline("Item regex##trade_regex", ref _tradeRegexBuffer, 2048u, new System.Numerics.Vector2(-1f, 60f));
		if (flag)
		{
			if (ImGui.Button("Create Trade profile##trade_create"))
			{
				RunProfileAction(delegate
				{
					EnsureTradeFieldsValid();
					TradeSearchProfile tradeSearchProfile = store.Create(_tradeProfileNameBuffer, TradeUrlValidator.Normalize(_tradeUrlBuffer), _tradeRegexBuffer.Trim());
					ApplyTradeProfile(tradeSearchProfile, logSelection: false);
					_log?.Info("PROFILE_CREATE", $"type=trade, name=\"{tradeSearchProfile.Name}\", count={store.Profiles.Count}");
				});
			}
			if (ImGui.Button("Cancel Trade profile##trade_cancel"))
			{
				ApplyTradeProfile(store.ActiveProfile, logSelection: false);
			}
		}
		else
		{
			if (ImGui.Button("New Trade profile##trade_new"))
			{
				BeginTradeCreation(store);
			}
			if (ImGui.Button("Save Trade profile##trade_save"))
			{
				RunProfileAction(delegate
				{
					EnsureTradeFieldsValid();
					TradeSearchProfile tradeSearchProfile = store.Update(active.Id, _tradeProfileNameBuffer, TradeUrlValidator.Normalize(_tradeUrlBuffer), _tradeRegexBuffer.Trim());
					ApplyTradeProfile(tradeSearchProfile, logSelection: false);
					_log?.Info("PROFILE_SAVE", "type=trade, name=\"" + tradeSearchProfile.Name + "\"");
				});
			}
		}
		ImGui.EndDisabled();
	}

	private void BeginSessionCreation(PoeSessionProfileStore store)
	{
		_sessionEditorMode = ProfileEditorMode.Creating;
		_sessionProfileNameBuffer = string.Empty;
		_poeSessionBuffer = string.Empty;
		_log?.Info("PROFILE_CREATE_BEGIN", $"type=session, count={store.Profiles.Count}");
	}

	private void BeginTradeCreation(TradeSearchProfileStore store)
	{
		_tradeEditorMode = ProfileEditorMode.Creating;
		_tradeProfileNameBuffer = string.Empty;
		_tradeUrlBuffer = string.Empty;
		_tradeRegexBuffer = string.Empty;
		_log?.Info("PROFILE_CREATE_BEGIN", $"type=trade, count={store.Profiles.Count}");
	}

	private void EnsureTradeFieldsValid()
	{
		if (!PoeTradeClient.TryParseTradeUrl(_tradeUrlBuffer, out TradeSearchContext _, out string error))
		{
			throw new InvalidOperationException(error);
		}
		if (string.IsNullOrWhiteSpace(_tradeRegexBuffer))
		{
			throw new InvalidOperationException("Item search expression is empty.");
		}
	}

	private void ApplySessionProfile(PoeSessionProfile profile, bool logSelection)
	{
		_sessionEditorMode = ProfileEditorMode.Viewing;
		_sessionProfileNameBuffer = profile.Name;
		_poeSessionBuffer = profile.PoeSessionId;
		_profileStatus = "Active POESESSID profile: " + profile.Name;
		if (logSelection)
		{
			_log?.Info("PROFILE_SELECT", "type=session, name=\"" + profile.Name + "\"");
		}
	}

	private void ApplyTradeProfile(TradeSearchProfile profile, bool logSelection)
	{
		_tradeEditorMode = ProfileEditorMode.Viewing;
		_tradeProfileNameBuffer = profile.Name;
		_tradeUrlBuffer = profile.TradeUrl;
		_tradeRegexBuffer = profile.ItemRegex;
		_profileStatus = "Active Trade profile: " + profile.Name;
		if (logSelection)
		{
			_log?.Info("PROFILE_SELECT", "type=trade, name=\"" + profile.Name + "\"");
		}
	}

	private void RunProfileAction(Action action)
	{
		try
		{
			action();
		}
		catch (Exception profileError)
		{
			SetProfileError(profileError);
		}
	}

	private void SetProfileError(Exception exception)
	{
		_profileStatus = exception.Message;
		_log?.Info("PROFILE_ERROR", exception.Message);
	}
}
