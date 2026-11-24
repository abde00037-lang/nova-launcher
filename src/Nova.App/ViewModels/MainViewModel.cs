using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
	private const int MaxConsoleLines = 250;

	private readonly Dispatcher _dispatcher;

	private readonly ConcurrentQueue<ConsoleLine> _pendingLines;

	private readonly DispatcherTimer _flushTimer;

	private InstanceVm? _selected;

	private bool _isRunning;

	private bool _isBusy;

	private string _statusText;

	private string _statusDetail;

	private double _statusProgress;

	private bool _statusIndeterminate;

	private string _headerTitle;

	private string _headerVersion;

	private string _headerMods;

	private string _javaSummary;

	private string _memorySummary;

	private string _playedSummary;

	private Process? _gameProcess;

	private LogTailer? _tailer;

	private string? _checksInstanceId;

	private bool _recheckPending;

	private DateTime _launchStartedUtc;

	private bool _crashBannerVisible;

	private string _crashTitle;

	private string _crashDetail;

	private string _crashMeta;

	private string _crashPath;

	private readonly List<ModRowVm> _allMods;

	private string _modFilter;

	private string _modsState;

	private int _modsSort;

	private string _modsMetaStatus;

	private TaskCompletionSource<bool>? _confirm;

	private string _confirmTitle;

	private string _confirmBody;

	private string _confirmYesLabel;

	private bool _confirmVisible;

	private readonly AccountStore _accountStore;

	private string _offlineName;

	private string _microsoftStatus;

	private string _deviceCode;

	private string _deviceLink;

	private bool _isMicrosoftSigningIn;

	private CancellationTokenSource? _signInCts;

	private readonly List<VersionRowVm> _allVersions;

	private bool _versionsLoaded;

	private bool _isVersionBusy;

	private string _versionFilter;

	private string _versionsMode;

	private string _versionStatus;

	private string _modsBrowseQuery;

	private string _modsBrowseStatus;

	private bool _modsBrowseMode;

	private bool _modsTargetPickerVisible;

	private bool _modsBrowseBusy;

	private bool _modsBrowseLoaded;

	private HashSet<string> _installedModTokens;

	private bool _browseIconsLoading;

	private string _storeQuery;

	private string _storeStatus;

	private bool _isStoreBusy;

	private string _storeSource;

	private static readonly string[] PopularStoreVersions = new string[15]
	{
		"1.21.4", "1.21.1", "1.21", "1.20.6", "1.20.1", "1.20", "1.19.2", "1.18.2", "1.17.1", "1.16.5",
		"1.15.2", "1.12.2", "1.10.2", "1.8.9", "1.7.10"
	};

	private bool _storeTargetPickerVisible;

	private int _storeOffset;

	private string _storeSort;

	private bool _hasMoreStore;

	private bool _iconsLoading;

	public LauncherPaths Paths { get; }

	public ObservableCollection<InstanceVm> Instances { get; }

	public ObservableCollection<ConsoleLine> ConsoleLines { get; }

	public ObservableCollection<CheckRow> Checks { get; }

	public ObservableCollection<ModRowVm> Mods { get; }

	public ObservableCollection<VersionRowVm> Versions { get; }

	public ObservableCollection<AccountRowVm> Accounts { get; }

	public ObservableCollection<InstancePickVm> InstancePicker { get; }

	public ObservableCollection<StoreItemVm> StoreItems { get; }

	public ObservableCollection<SimpleRow> Worlds { get; }

	public ObservableCollection<SimpleRow> Backups { get; }

	public ICommand LaunchCommand { get; }

	public ICommand StopCommand { get; }

	public ICommand CheckCommand { get; }

	public ICommand RepairCommand { get; }

	public ICommand BackupCommand { get; }

	public ICommand RefreshCommand { get; }

	public ICommand OpenFolderCommand { get; }

	public ICommand SearchStoreCommand { get; }

	public ICommand RefreshVersionsCommand { get; }

	public ICommand AddOfflineCommand { get; }

	public ICommand SignInMicrosoftCommand { get; }

	public ICommand ConfirmYesCommand { get; }

	public ICommand ConfirmNoCommand { get; }

	public ICommand OpenStoreTargetPickerCommand { get; }

	public ICommand OpenModsTargetPickerCommand { get; }

	public ICommand CloseModsTargetPickerCommand { get; }

	public ICommand SearchModBrowseCommand { get; }

	public ICommand CloseStoreTargetPickerCommand { get; }

	public ICommand InstallStoreItemCommand { get; }

	public ICommand OpenCrashReportCommand { get; }

	public ICommand OpenCrashFolderCommand { get; }

	public ICommand DisableCrashSuspectCommand { get; }

	public ICommand DismissCrashCommand { get; }

	public ICommand CheckLauncherUpdateCommand { get; }

	public ICommand ApplyLauncherUpdateCommand { get; }

	public ICommand OpenLauncherReleaseUrlCommand { get; }

	public ICommand DismissLauncherUpdateCommand { get; }

	private LauncherUpdateInfo? _pendingLauncherUpdate;

	private bool _launcherUpdateBannerVisible;

	private bool _isLauncherUpdateBusy;

	private string _launcherUpdateStatus = "Up to date · v" + LauncherUpdater.CurrentVersion;

	public string LauncherVersionLabel => "v" + LauncherUpdater.CurrentVersion;

	public string UpdateRepoSlug
	{
		get => string.IsNullOrWhiteSpace(Settings.UpdateRepo) ? LauncherUpdater.DefaultRepo : Settings.UpdateRepo;
		set
		{
			Settings.UpdateRepo = (string.IsNullOrWhiteSpace(value) ? LauncherUpdater.DefaultRepo : value.Trim().Trim('/'));
			Settings.Save(Paths);
			Raise("UpdateRepoSlug");
		}
	}

	public bool AutoCheckLauncherUpdates
	{
		get => Settings.AutoCheckUpdates;
		set
		{
			Settings.AutoCheckUpdates = value;
			Settings.Save(Paths);
			Raise("AutoCheckLauncherUpdates");
		}
	}

	public bool LauncherUpdateBannerVisible
	{
		get => _launcherUpdateBannerVisible;
		private set => Set(ref _launcherUpdateBannerVisible, value, "LauncherUpdateBannerVisible");
	}

	public bool HasLauncherUpdate => _pendingLauncherUpdate != null;

	public bool IsLauncherUpdateBusy
	{
		get => _isLauncherUpdateBusy;
		private set
		{
			if (Set(ref _isLauncherUpdateBusy, value, "IsLauncherUpdateBusy"))
			{
				Raise("CanCheckLauncherUpdate");
				Raise("CanApplyLauncherUpdate");
				Raise("LauncherUpdateActionLabel");
				(CheckLauncherUpdateCommand as RelayCommand)?.Refresh();
				(ApplyLauncherUpdateCommand as RelayCommand)?.Refresh();
			}
		}
	}

	public bool CanCheckLauncherUpdate => !_isLauncherUpdateBusy;

	public bool CanApplyLauncherUpdate => !_isLauncherUpdateBusy && _pendingLauncherUpdate != null && !string.IsNullOrWhiteSpace(_pendingLauncherUpdate.DownloadUrl) && !IsRunning;

	public string LauncherUpdateTitle => (_pendingLauncherUpdate == null)
		? ("NOVA WORKBENCH · v" + LauncherUpdater.CurrentVersion)
		: $"UPDATE AVAILABLE · v{_pendingLauncherUpdate.CurrentVersion} → v{_pendingLauncherUpdate.LatestVersion}";

	public string LauncherUpdateDetail
	{
		get
		{
			if (_pendingLauncherUpdate == null)
			{
				return _launcherUpdateStatus;
			}
			string notes = (_pendingLauncherUpdate.ReleaseNotes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
			if (notes.Length > 150)
			{
				notes = notes.Substring(0, 150) + "…";
			}
			return string.IsNullOrWhiteSpace(notes)
				? (_pendingLauncherUpdate.Title + " — worlds, mods, accounts, and instances are 100% preserved.")
				: notes;
		}
	}

	public string LauncherUpdateStatus
	{
		get => _launcherUpdateStatus;
		private set
		{
			if (Set(ref _launcherUpdateStatus, value, "LauncherUpdateStatus"))
			{
				Raise("LauncherUpdateDetail");
			}
		}
	}

	public string LauncherUpdateActionLabel => _isLauncherUpdateBusy
		? "DOWNLOADING…"
		: ((_pendingLauncherUpdate != null) ? $"UPDATE TO v{_pendingLauncherUpdate.LatestVersion} & RESTART" : "UPDATE & RESTART");

	private string _crashSuspectFile = "";

	public string CrashSuspectFile
	{
		get => _crashSuspectFile;
		private set
		{
			if (Set(ref _crashSuspectFile, value, "CrashSuspectFile"))
			{
				Raise("HasCrashSuspect");
				Raise("DisableSuspectLabel");
				(DisableCrashSuspectCommand as RelayCommand)?.Refresh();
			}
		}
	}

	public bool HasCrashSuspect => !string.IsNullOrWhiteSpace(_crashSuspectFile);

	public string DisableSuspectLabel => string.IsNullOrWhiteSpace(_crashSuspectFile)
		? "DISABLE SUSPECT"
		: ("DISABLE " + ModManager.DisplayName(_crashSuspectFile));

	private bool _isModUpdateBusy;

	private int _pendingModUpdatesCount;

	public bool IsModUpdateBusy
	{
		get => _isModUpdateBusy;
		private set
		{
			if (Set(ref _isModUpdateBusy, value, "IsModUpdateBusy"))
			{
				Raise("CanCheckModUpdates");
				Raise("CanUpdateAllMods");
			}
		}
	}

	public int PendingModUpdatesCount
	{
		get => _pendingModUpdatesCount;
		private set
		{
			if (Set(ref _pendingModUpdatesCount, value, "PendingModUpdatesCount"))
			{
				Raise("HasPendingModUpdates");
				Raise("CanUpdateAllMods");
				Raise("UpdateAllModsLabel");
			}
		}
	}

	public bool HasPendingModUpdates => _pendingModUpdatesCount > 0;

	public bool CanCheckModUpdates => !_isModUpdateBusy && Selected != null;

	public bool CanUpdateAllMods => !_isModUpdateBusy && _pendingModUpdatesCount > 0;

	public string UpdateAllModsLabel => $"UPDATE ALL ({_pendingModUpdatesCount})";

	public bool CrashBannerVisible
	{
		get
		{
			return _crashBannerVisible;
		}
		private set
		{
			Set(ref _crashBannerVisible, value, "CrashBannerVisible");
		}
	}

	public string CrashTitle
	{
		get
		{
			return _crashTitle;
		}
		private set
		{
			Set(ref _crashTitle, value, "CrashTitle");
		}
	}

	public string CrashDetail
	{
		get
		{
			return _crashDetail;
		}
		private set
		{
			Set(ref _crashDetail, value, "CrashDetail");
		}
	}

	public string CrashMeta
	{
		get
		{
			return _crashMeta;
		}
		private set
		{
			Set(ref _crashMeta, value, "CrashMeta");
		}
	}

	public string CrashPath
	{
		get
		{
			return _crashPath;
		}
		private set
		{
			if (Set(ref _crashPath, value, "CrashPath"))
			{
				Raise("CrashHasReport");
			}
		}
	}

	public bool CrashHasReport => !string.IsNullOrWhiteSpace(_crashPath);

	public InstanceVm? Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			if (Set(ref _selected, value, "Selected"))
			{
				OnSelectionChanged();
				RefreshCommands();
			}
		}
	}

	public bool IsRunning
	{
		get
		{
			return _isRunning;
		}
		private set
		{
			if (Set(ref _isRunning, value, "IsRunning"))
			{
				Raise("PlayLabel");
				RefreshCommands();
			}
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			if (Set(ref _isBusy, value, "IsBusy"))
			{
				RefreshCommands();
			}
		}
	}

	public string PlayLabel
	{
		get
		{
			if (!IsRunning)
			{
				return "PLAY";
			}
			return "RUNNING…";
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		private set
		{
			Set(ref _statusText, value, "StatusText");
		}
	}

	public string StatusDetail
	{
		get
		{
			return _statusDetail;
		}
		private set
		{
			Set(ref _statusDetail, value, "StatusDetail");
		}
	}

	public double StatusProgress
	{
		get
		{
			return _statusProgress;
		}
		private set
		{
			Set(ref _statusProgress, value, "StatusProgress");
		}
	}

	public bool StatusIndeterminate
	{
		get
		{
			return _statusIndeterminate;
		}
		private set
		{
			Set(ref _statusIndeterminate, value, "StatusIndeterminate");
		}
	}

	public string HeaderTitle
	{
		get
		{
			return _headerTitle;
		}
		private set
		{
			Set(ref _headerTitle, value, "HeaderTitle");
		}
	}

	public string HeaderVersion
	{
		get
		{
			return _headerVersion;
		}
		private set
		{
			Set(ref _headerVersion, value, "HeaderVersion");
		}
	}

	public string HeaderMods
	{
		get
		{
			return _headerMods;
		}
		private set
		{
			Set(ref _headerMods, value, "HeaderMods");
		}
	}

	public string JavaSummary
	{
		get
		{
			return _javaSummary;
		}
		private set
		{
			Set(ref _javaSummary, value, "JavaSummary");
		}
	}

	public string MemorySummary
	{
		get
		{
			return _memorySummary;
		}
		private set
		{
			Set(ref _memorySummary, value, "MemorySummary");
		}
	}

	public string PlayedSummary
	{
		get
		{
			return _playedSummary;
		}
		private set
		{
			Set(ref _playedSummary, value, "PlayedSummary");
		}
	}

	public bool AutoCheckEnabled { get; set; }

	public string ModFilter
	{
		get
		{
			return _modFilter;
		}
		set
		{
			if (Set(ref _modFilter, value, "ModFilter"))
			{
				ApplyModFilter();
			}
		}
	}

	public string ModsState
	{
		get
		{
			return _modsState;
		}
		set
		{
			if (Set(ref _modsState, value, "ModsState"))
			{
				ApplyModFilter();
			}
		}
	}

	public string ModsSortLabel => _modsSort switch
	{
		1 => "NAME Z → A", 
		2 => "SMALLEST FIRST", 
		3 => "LARGEST FIRST", 
		_ => "NAME A → Z", 
	};

	public string ModsShownLabel
	{
		get
		{
			if (Mods.Count == _allMods.Count)
			{
				return $"{_allMods.Count} mods";
			}
			return $"{Mods.Count} of {_allMods.Count} shown";
		}
	}

	public string ModsSummary
	{
		get
		{
			int num = Enumerable.Count(_allMods, (ModRowVm m) => m.IsEnabled);
			int num2 = _allMods.Count - num;
			if (num2 == 0)
			{
				return $"{num} mods · all enabled";
			}
			return $"{num} enabled · {num2} disabled · {_allMods.Count} files";
		}
	}

	public string ModsMetaStatus
	{
		get
		{
			return _modsMetaStatus;
		}
		private set
		{
			Set(ref _modsMetaStatus, value, "ModsMetaStatus");
		}
	}

	public string ConfirmTitle
	{
		get
		{
			return _confirmTitle;
		}
		private set
		{
			Set(ref _confirmTitle, value, "ConfirmTitle");
		}
	}

	public string ConfirmBody
	{
		get
		{
			return _confirmBody;
		}
		private set
		{
			Set(ref _confirmBody, value, "ConfirmBody");
		}
	}

	public string ConfirmYesLabel
	{
		get
		{
			return _confirmYesLabel;
		}
		private set
		{
			Set(ref _confirmYesLabel, value, "ConfirmYesLabel");
		}
	}

	public bool ConfirmVisible
	{
		get
		{
			return _confirmVisible;
		}
		private set
		{
			Set(ref _confirmVisible, value, "ConfirmVisible");
		}
	}

	public string InstancePickerSummary => Instances.Count switch
	{
		0 => "no instances yet", 
		1 => "1 installed instance", 
		_ => Instances.Count + " installed instances", 
	};

	public string OfflineName
	{
		get
		{
			return _offlineName;
		}
		set
		{
			Set(ref _offlineName, value, "OfflineName");
		}
	}

	public string MicrosoftStatus
	{
		get
		{
			return _microsoftStatus;
		}
		set
		{
			Set(ref _microsoftStatus, value, "MicrosoftStatus");
		}
	}

	public string DeviceCode
	{
		get
		{
			return _deviceCode;
		}
		private set
		{
			if (Set(ref _deviceCode, value, "DeviceCode"))
			{
				Raise("HasDeviceCode");
			}
		}
	}

	public bool HasDeviceCode => !string.IsNullOrWhiteSpace(_deviceCode);

	public string DeviceLink
	{
		get
		{
			return _deviceLink;
		}
		private set
		{
			Set(ref _deviceLink, value, "DeviceLink");
		}
	}

	public bool CanSignInMicrosoft => !_isMicrosoftSigningIn;

	public string MicrosoftClientId
	{
		get
		{
			return Settings.MicrosoftClientId ?? "";
		}
		set
		{
			Settings.MicrosoftClientId = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			Settings.Save(Paths);
			Raise("MicrosoftClientId");
			Raise("CanSignInMicrosoft");
		}
	}

	public string AccountsSummary
	{
		get
		{
			if (Accounts.Count != 0)
			{
				return $"{Accounts.Count} account{((Accounts.Count == 1) ? "" : "s")} saved · {Enumerable.Count(Accounts, (AccountRowVm a) => a.IsMicrosoft)} microsoft";
			}
			return "no accounts saved yet";
		}
	}

	public string ActiveAccountLabel => "playing as " + ActiveAccount().Name;

	public string HeaderAccount => ActiveAccount().Name;

	public string VersionFilter
	{
		get
		{
			return _versionFilter;
		}
		set
		{
			if (Set(ref _versionFilter, value, "VersionFilter"))
			{
				ApplyVersionFilter();
			}
		}
	}

	private double _versionProgress;

	private string _versionActiveLabel = "";

	public double VersionProgress
	{
		get
		{
			return _versionProgress;
		}
		private set
		{
			if (Set(ref _versionProgress, Math.Clamp(value, 0.0, 1.0), "VersionProgress"))
			{
				Raise("VersionProgressPercent");
			}
		}
	}

	public string VersionProgressPercent => $"{(int)Math.Round(_versionProgress * 100.0)}%";

	public string VersionActiveLabel
	{
		get
		{
			return _versionActiveLabel;
		}
		private set
		{
			Set(ref _versionActiveLabel, value, "VersionActiveLabel");
		}
	}

	public bool IsVersionBusy
	{
		get
		{
			return _isVersionBusy;
		}
		private set
		{
			if (Set(ref _isVersionBusy, value, "IsVersionBusy"))
			{
				Raise("CanInstallVersion");
				(RefreshVersionsCommand as RelayCommand)?.Refresh();
			}
		}
	}

	public bool CanInstallVersion => !_isVersionBusy;

	public string VersionsModeLabel
	{
		get
		{
			return _versionsMode switch
			{
				"Forge" => "forge builds",
				"Fabric" => "fabric builds",
				"NeoForge" => "neoforge builds",
				_ => "official releases"
			};
		}
	}

	public string VersionStatus
	{
		get
		{
			return _versionStatus;
		}
		private set
		{
			Set(ref _versionStatus, value, "VersionStatus");
		}
	}

	public string VersionsSummary
	{
		get
		{
			int value = Enumerable.Count(_allVersions, (VersionRowVm v) => v.Installed);
			return $"{_allVersions.Count} versions · {value} downloaded";
		}
	}

	public string VersionsShown
	{
		get
		{
			if (Versions.Count == _allVersions.Count)
			{
				return $"{_allVersions.Count} shown";
			}
			return $"{Versions.Count} shown";
		}
	}

	public ObservableCollection<StoreItemVm> ModBrowseItems { get; }

	public ObservableCollection<ForgeTargetVm> ForgeTargets { get; }

	public string ModsTargetVersion
	{
		get
		{
			return Settings.ModsTargetVersion ?? "";
		}
		set
		{
			string modsTargetVersion = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			Settings.ModsTargetVersion = modsTargetVersion;
			Settings.Save(Paths);
			Raise("ModsTargetVersion");
			Raise("ModsTargetIsAuto");
			Raise("ModsTargetButtonLabel");
			Raise("ModsBrowseTargetLine");
		}
	}

	public bool ModsTargetIsAuto => string.IsNullOrWhiteSpace(ModsTargetVersion);

	public string ModsTargetVersionId
	{
		get
		{
			if (!ModsTargetIsAuto)
			{
				return ModsTargetVersion;
			}
			if (Selected != null && ModStore.LoaderOf(Selected.VersionId) != "vanilla")
			{
				return Selected.VersionId;
			}
			return Instances.FirstOrDefault((InstanceVm i) => ModStore.LoaderOf(i.VersionId) != "vanilla")?.VersionId ?? "";
		}
	}

	private InstanceVm? ModsTargetInstance => Instances.FirstOrDefault((InstanceVm i) => string.Equals(i.VersionId, ModsTargetVersionId, StringComparison.OrdinalIgnoreCase));

	public string ModsTargetButtonLabel
	{
		get
		{
			if (!ModsTargetIsAuto)
			{
				return "VERSION · " + ShortVersion(ModsTargetVersionId);
			}
			return "VERSION · AUTO";
		}
	}

	public string ModsBrowseTargetLine
	{
		get
		{
			string modsTargetVersionId = ModsTargetVersionId;
			if (string.IsNullOrWhiteSpace(modsTargetVersionId))
			{
				return "no Forge version downloaded yet - get one from Versions";
			}
			InstanceVm modsTargetInstance = ModsTargetInstance;
			if (modsTargetInstance != null)
			{
				return ShortVersion(modsTargetVersionId) + "  ·  installing into " + modsTargetInstance.Name;
			}
			return ShortVersion(modsTargetVersionId) + "  ·  no instance yet: the first download creates one for you";
		}
	}

	public bool ModsBrowseMode
	{
		get
		{
			return _modsBrowseMode;
		}
		set
		{
			if (Set(ref _modsBrowseMode, value, "ModsBrowseMode"))
			{
				Raise("ModsInstalledMode");
				if (value && !_modsBrowseLoaded)
				{
					_modsBrowseLoaded = true;
					SearchModBrowseAsync();
				}
			}
		}
	}

	public bool ModsInstalledMode => !_modsBrowseMode;

	public string ModsBrowseQuery
	{
		get
		{
			return _modsBrowseQuery;
		}
		set
		{
			Set(ref _modsBrowseQuery, value, "ModsBrowseQuery");
		}
	}

	public string ModsBrowseStatus
	{
		get
		{
			return _modsBrowseStatus;
		}
		private set
		{
			Set(ref _modsBrowseStatus, value, "ModsBrowseStatus");
		}
	}

	public bool ModsTargetPickerVisible
	{
		get
		{
			return _modsTargetPickerVisible;
		}
		set
		{
			Set(ref _modsTargetPickerVisible, value, "ModsTargetPickerVisible");
		}
	}

	public LauncherSettings Settings { get; }

	public string[] StoreSources { get; }

	public string StoreQuery
	{
		get
		{
			return _storeQuery;
		}
		set
		{
			Set(ref _storeQuery, value, "StoreQuery");
		}
	}

	public string SourceName
	{
		get
		{
			return _storeSource;
		}
		set
		{
			if (Set(ref _storeSource, value, "SourceName"))
			{
				Raise("StoreSourceIsCurse");
			}
		}
	}

	public bool StoreSourceIsCurse => string.Equals(_storeSource, "CurseForge", StringComparison.OrdinalIgnoreCase);

	public string StoreStatus
	{
		get
		{
			return _storeStatus;
		}
		private set
		{
			Set(ref _storeStatus, value, "StoreStatus");
		}
	}

	public bool IsStoreBusy
	{
		get
		{
			return _isStoreBusy;
		}
		private set
		{
			Set(ref _isStoreBusy, value, "IsStoreBusy");
		}
	}

	public string StoreTargetVersion
	{
		get
		{
			return Settings.StoreTargetVersion ?? "";
		}
		set
		{
			string storeTargetVersion = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			Settings.StoreTargetVersion = storeTargetVersion;
			Settings.Save(Paths);
			Raise("StoreTargetVersion");
			Raise("StoreTargetChoiceLabel");
			Raise("StoreTargetLabel");
			Raise("StoreAutoIsOn");
			Raise("StoreHasFixedTarget");
		}
	}

	public bool StoreAutoIsOn => string.IsNullOrWhiteSpace(StoreTargetVersion);

	public bool StoreHasFixedTarget => !StoreAutoIsOn;

	public ObservableCollection<StoreVersionVm> StoreVersionChoices { get; }

	public string StoreTargetChoiceLabel
	{
		get
		{
			if (!StoreAutoIsOn)
			{
				return "MODS FOR · " + StoreTargetVersion;
			}
			return "MODS FOR · AUTO";
		}
	}

	private string EffectiveStoreVersion
	{
		get
		{
			if (!StoreAutoIsOn)
			{
				return StoreTargetVersion;
			}
			return Selected?.McVersion ?? "";
		}
	}

	private string EffectiveStoreLoader
	{
		get
		{
			if (StoreAutoIsOn)
			{
				if (Selected != null)
				{
					return ModStore.LoaderOf(Selected.VersionId);
				}
				return "forge";
			}
			InstanceVm storeInstallTarget = StoreInstallTarget;
			if (storeInstallTarget != null)
			{
				return ModStore.LoaderOf(storeInstallTarget.VersionId);
			}
			return "forge";
		}
	}

	public InstanceVm? StoreInstallTarget
	{
		get
		{
			if (StoreAutoIsOn)
			{
				return Selected;
			}
			string version = EffectiveStoreVersion;
			if (Selected != null && string.Equals(Selected.McVersion, version, StringComparison.OrdinalIgnoreCase))
			{
				return Selected;
			}
			return Instances.FirstOrDefault((InstanceVm i) => string.Equals(i.McVersion, version, StringComparison.OrdinalIgnoreCase));
		}
	}

	public string StoreTargetLabel
	{
		get
		{
			if (Selected == null && StoreAutoIsOn)
			{
				return "select an instance, or pick a version below";
			}
			string effectiveStoreVersion = EffectiveStoreVersion;
			string effectiveStoreLoader = EffectiveStoreLoader;
			InstanceVm storeInstallTarget = StoreInstallTarget;
			string text = (StoreAutoIsOn ? ("auto-filtered: only mods that run on " + effectiveStoreVersion + " with " + effectiveStoreLoader) : $"mods for {effectiveStoreVersion} ({effectiveStoreLoader}) - picked by hand");
			if (storeInstallTarget != null)
			{
				return text + "  ·  installing into " + storeInstallTarget.Name;
			}
			return text + "  ·  no instance for " + effectiveStoreVersion + " yet: search works, installing needs an instance of that version";
		}
	}

	public bool StoreTargetPickerVisible
	{
		get
		{
			return _storeTargetPickerVisible;
		}
		set
		{
			Set(ref _storeTargetPickerVisible, value, "StoreTargetPickerVisible");
		}
	}

	public string StoreSortLabel
	{
		get
		{
			string storeSort = _storeSort;
			if (!(storeSort == "newest"))
			{
				if (storeSort == "name")
				{
					return "A → Z";
				}
				return "MOST DOWNLOADED";
			}
			return "NEWEST";
		}
	}

	public bool HasMoreStore
	{
		get
		{
			return _hasMoreStore;
		}
		private set
		{
			if (Set(ref _hasMoreStore, value, "HasMoreStore"))
			{
				Raise("CanLoadMore");
			}
		}
	}

	public bool CanLoadMore
	{
		get
		{
			if (_hasMoreStore)
			{
				return !IsStoreBusy;
			}
			return false;
		}
	}

	public string AccentName
	{
		get
		{
			return Settings.Accent;
		}
		set
		{
			if (!string.Equals(Settings.Accent, value, StringComparison.OrdinalIgnoreCase))
			{
				Settings.Accent = value;
				Settings.Save(Paths);
				ThemeManager.Apply(value);
				Raise("AccentName");
			}
		}
	}

	public string CurseForgeKey
	{
		get
		{
			return Settings.CurseForgeKey ?? "";
		}
		set
		{
			Settings.CurseForgeKey = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			Settings.Save(Paths);
			Raise("CurseForgeKey");
			Raise("CurseForgeConfigured");
		}
	}

	public bool CurseForgeConfigured => !string.IsNullOrWhiteSpace(Settings.CurseForgeKey);

	public bool IsGameRunning => IsRunning;

	public event Action<string>? Navigate;

	public MainViewModel(LauncherPaths paths)
	{
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d5: Expected Obj, but got Unknown
		Application current = Application.Current;
		_dispatcher = ((current != null) ? ((DispatcherObject)current).Dispatcher : null) ?? Dispatcher.CurrentDispatcher;
		_pendingLines = new ConcurrentQueue<ConsoleLine>();
		_statusText = "Ready";
		_statusDetail = "Select an instance to start.";
		_headerTitle = "No instance";
		_headerVersion = "-";
		_headerMods = "-";
		_javaSummary = "-";
		_memorySummary = "-";
		_playedSummary = "-";
		_launchStartedUtc = DateTime.UtcNow;
		_crashTitle = "";
		_crashDetail = "";
		_crashMeta = "";
		_crashPath = "";
		_allMods = new List<ModRowVm>();
		_modFilter = "";
		_modsState = "All";
		_modsMetaStatus = "";
		_confirmTitle = "";
		_confirmBody = "";
		_confirmYesLabel = "DELETE";
		_offlineName = "Player";
		_microsoftStatus = "";
		_deviceCode = "";
		_deviceLink = "https://microsoft.com/link";
		_allVersions = new List<VersionRowVm>();
		_versionFilter = "";
		_versionsMode = "Minecraft";
		_versionStatus = "press refresh to load the version list";
		_modsBrowseQuery = "";
		_modsBrowseStatus = "pick a Forge version, then search";
		_installedModTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		_storeQuery = "";
		_storeStatus = "Type a name and press search - results match the selected instance.";
		_storeSource = "Modrinth";
		StoreSources = new string[2] { "Modrinth", "CurseForge" };
		StoreVersionChoices = new ObservableCollection<StoreVersionVm>();
		_storeSort = "downloads";
		Paths = paths;
		Settings = LauncherSettings.Load(paths);
		JavaLocator.RegisterRoot(paths.JavaHome);
		_accountStore = new AccountStore(paths);
		if (string.Equals(Settings.StoreSource, "CurseForge", StringComparison.OrdinalIgnoreCase))
		{
			_storeSource = "CurseForge";
		}
		Instances = new ObservableCollection<InstanceVm>();
		ConsoleLines = new ObservableCollection<ConsoleLine>();
		Checks = new ObservableCollection<CheckRow>();
		Mods = new ObservableCollection<ModRowVm>();
		Versions = new ObservableCollection<VersionRowVm>();
		Accounts = new ObservableCollection<AccountRowVm>();
		InstancePicker = new ObservableCollection<InstancePickVm>();
		ModBrowseItems = new ObservableCollection<StoreItemVm>();
		ForgeTargets = new ObservableCollection<ForgeTargetVm>();
		StoreItems = new ObservableCollection<StoreItemVm>();
		Worlds = new ObservableCollection<SimpleRow>();
		Backups = new ObservableCollection<SimpleRow>();
		_flushTimer = new DispatcherTimer((DispatcherPriority)4, _dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(140.0)
		};
		_flushTimer.Tick += (object? _, EventArgs _) =>
		{
			FlushConsole();
		};
		_flushTimer.Start();
		LaunchCommand = new RelayCommand(async () =>
		{
			await LaunchAsync();
		}, () => Selected != null && !IsRunning);
		StopCommand = new RelayCommand(StopGame, () => IsRunning);
		CheckCommand = new RelayCommand(async () =>
		{
			await CheckAsync();
		}, () => Selected != null && !IsBusy);
		RepairCommand = new RelayCommand(async () =>
		{
			await RepairAsync();
		}, () => Selected != null && !IsBusy);
		BackupCommand = new RelayCommand(async () =>
		{
			await BackupAsync();
		}, () => Selected != null && !IsBusy);
		RefreshCommand = new RelayCommand(async () =>
		{
			await RefreshAsync();
		});
		OpenFolderCommand = new RelayCommand(OpenGameFolder, () => Selected != null);
		SearchStoreCommand = new RelayCommand(async () =>
		{
			await SearchStoreAsync();
		}, () => !IsStoreBusy);
		RefreshVersionsCommand = new RelayCommand(async () =>
		{
			await LoadVersionsAsync(force: true);
		}, () => !IsVersionBusy);
		AddOfflineCommand = new RelayCommand(AddOfflineAccount);
		OpenModsTargetPickerCommand = new RelayCommand(() =>
		{
			RefreshForgeTargets();
			ModsTargetPickerVisible = true;
		});
		CloseModsTargetPickerCommand = new RelayCommand(() =>
		{
			ModsTargetPickerVisible = false;
		});
		OpenCrashReportCommand = new RelayCommand(OpenCrashReport, () => CrashHasReport);
		OpenCrashFolderCommand = new RelayCommand(OpenCrashFolder);
		DisableCrashSuspectCommand = new RelayCommand(DisableCrashSuspect, () => HasCrashSuspect);
		DismissCrashCommand = new RelayCommand(() =>
		{
			CrashBannerVisible = false;
		});
		CheckLauncherUpdateCommand = new RelayCommand(async () =>
		{
			await CheckLauncherUpdateAsync(manual: true);
		}, () => CanCheckLauncherUpdate);
		ApplyLauncherUpdateCommand = new RelayCommand(async () =>
		{
			await ApplyLauncherUpdateAsync();
		}, () => CanApplyLauncherUpdate);
		OpenLauncherReleaseUrlCommand = new RelayCommand(OpenLauncherReleaseUrl);
		DismissLauncherUpdateCommand = new RelayCommand(() =>
		{
			LauncherUpdateBannerVisible = false;
		});
		SearchModBrowseCommand = new RelayCommand(async () =>
		{
			await SearchModBrowseAsync();
		});
		OpenStoreTargetPickerCommand = new RelayCommand(() =>
		{
			RefreshStoreVersionChoices();
			StoreTargetPickerVisible = true;
		});
		CloseStoreTargetPickerCommand = new RelayCommand(() =>
		{
			StoreTargetPickerVisible = false;
		});
		ConfirmYesCommand = new RelayCommand(() =>
		{
			ResolveConfirm(answer: true);
		});
		ConfirmNoCommand = new RelayCommand(() =>
		{
			ResolveConfirm(answer: false);
		});
		SignInMicrosoftCommand = new RelayCommand(SignInWithMicrosoft);
		InstallStoreItemCommand = new RelayCommand(async (object? parameter) =>
		{
			if (parameter is StoreItemVm item)
			{
				await InstallStoreAsync(item);
			}
		});
		Log($"launcher v{LauncherUpdater.CurrentVersion} started, root = {paths.Root}");
		AppendConsole("OK", $"Nova Workbench v{LauncherUpdater.CurrentVersion} started. Portable root: {paths.Root}");
		AppendConsole("INFO", $"Update channel: github.com/{UpdateRepoSlug}");
		AppendConsole("INFO", "Assets, libraries, mods, and worlds are never deleted during launcher updates.");
		FlushConsole();
		if (Settings.AutoCheckUpdates)
		{
			_ = CheckLauncherUpdateAsync(manual: false);
		}
	}

	private void RefreshCommands()
	{
		ICommand[] array = new ICommand[6] { LaunchCommand, StopCommand, CheckCommand, RepairCommand, BackupCommand, OpenFolderCommand };
		for (int i = 0; i < array.Length; i++)
		{
			(array[i] as RelayCommand)?.Refresh();
		}
	}

	private void RunOnUi(Action action)
	{
		if (_dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			_dispatcher.BeginInvoke((Delegate)action, (DispatcherPriority)4, Array.Empty<object>());
		}
	}

	private Task RunOnUiAsync(Action action)
	{
		if (_dispatcher.CheckAccess())
		{
			action();
			return Task.CompletedTask;
		}
		return _dispatcher.InvokeAsync(action, (DispatcherPriority)4).Task;
	}

	private static void Log(string message)
	{
		try
		{
			string text = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "logs"));
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "nova-ui.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	public async Task RefreshAsync()
	{
		List<InstanceVm> list = await Task.Run(() =>
		{
			InstanceStore instanceStore = new InstanceStore(Paths);
			JavaLocator.Discover();
			List<InstanceVm> list2 = new List<InstanceVm>();
			foreach (InstanceConfig item in instanceStore.All())
			{
				string path = Path.Combine(item.GameDirectory, "mods");
				int modCount = ModManager.CountInstalled(path);
				string minecraftVersion = ModStore.McVersionOf(item.VersionId);
				JavaInstall javaInstall = ((!string.IsNullOrWhiteSpace(item.JavaPath) && File.Exists(item.JavaPath)) ? JavaLocator.Probe(item.JavaPath) : JavaLocator.Pick(VersionResolver.GuessJavaMajor(minecraftVersion)));
				list2.Add(new InstanceVm(item, modCount, ((object)javaInstall == null) ? "Java not found" : $"Java {javaInstall.Major} · {javaInstall.RawVersion}", javaInstall));
			}
			return list2;
		});
		await RunOnUiAsync(() =>
		{
			string previousId = Selected?.Id;
			Instances.Clear();
			foreach (InstanceVm item2 in list)
			{
				Instances.Add(item2);
			}
			Selected = Instances.FirstOrDefault((InstanceVm i) => i.Id == previousId) ?? Instances.FirstOrDefault();
			RaiseModsTargetState();
		});
	}

	private void OnSelectionChanged()
	{
		RaiseModsTargetState();
		Raise("CanCheckModUpdates");
		PendingModUpdatesCount = 0;
		ShowRememberedCrash();
		if (Selected == null)
		{
			HeaderTitle = "No instance";
			HeaderVersion = "-";
			HeaderMods = "-";
			JavaSummary = "-";
			MemorySummary = "-";
			PlayedSummary = "-";
			Raise("StoreTargetLabel");
			return;
		}
		Raise("StoreTargetLabel");
		HeaderTitle = Selected.Name;
		HeaderVersion = Selected.VersionId;
		HeaderMods = Selected.ModCount + " mods";
		JavaSummary = Selected.JavaLabel;
		MemorySummary = Selected.MemoryLabel;
		PlayedSummary = Selected.LastPlayed;
		if (_checksInstanceId != null && !string.Equals(_checksInstanceId, Selected.Id, StringComparison.OrdinalIgnoreCase))
		{
			Checks.Clear();
			_checksInstanceId = null;
		}
		if (!IsRunning)
		{
			StatusText = "Ready";
			StatusDetail = "Run a health check, then press PLAY.";
			StatusProgress = 0.0;
			StatusIndeterminate = false;
		}
		LoadInstanceDetailsAsync(Selected);
		RequestAutoCheck();
	}

	public void RequestNavigate(string page)
	{
		Log("navigate -> " + page);
		Navigate?.Invoke(page);
	}

	public void RequestAutoCheck()
	{
		if (AutoCheckEnabled && Selected != null && (Checks.Count <= 0 || !string.Equals(_checksInstanceId, Selected.Id, StringComparison.OrdinalIgnoreCase)))
		{
			if (IsBusy || IsRunning)
			{
				_recheckPending = true;
			}
			else
			{
				CheckAsync();
			}
		}
	}

	private async Task LoadInstanceDetailsAsync(InstanceVm instance)
	{
		string modsDir = Path.Combine(instance.GameDirectory, "mods");
		List<BackupInfo> backups = WorldBackup.List(Paths, instance.Id);
		List<string> worlds = WorldBackup.ListWorlds(instance.GameDirectory);
		await Task.Run(() =>
		{
			ModScanResult modScanResult = (Directory.Exists(modsDir) ? ModScanner.Scan(modsDir) : new ModScanResult());
			string path = Path.Combine(instance.GameDirectory, "saves");
			List<SimpleRow> worldRows = new List<SimpleRow>();
			foreach (string item in worlds)
			{
				string path2 = Path.Combine(path, item);
				long num = 0L;
				DateTime dateTime;
				try
				{
					DirectoryInfo directoryInfo = new DirectoryInfo(path2);
					dateTime = directoryInfo.LastWriteTime;
					foreach (FileInfo item2 in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
					{
						num += item2.Length;
					}
				}
				catch
				{
					dateTime = DateTime.MinValue;
				}
				double value = Math.Round((double)num / 1024.0 / 1024.0, 1);
				string value2 = ((dateTime == DateTime.MinValue) ? "unknown date" : dateTime.ToString("yyyy-MM-dd HH:mm"));
				worldRows.Add(new SimpleRow(item, "", $"{value2}  ·  {value} MB"));
			}
			List<ModEntry> scanSnapshot = modScanResult.Mods.ToList();
			List<ModRowVm> modRows = modScanResult.Mods.OrderBy((ModEntry m) => m.Name ?? m.FileName, StringComparer.OrdinalIgnoreCase).Select((ModEntry mod) =>
			{
				long sizeBytes = 0L;
				try
				{
					sizeBytes = new FileInfo(Path.Combine(modsDir, mod.FileName)).Length;
				}
				catch
				{
				}
				return new ModRowVm(mod.FileName, mod.Name ?? ModManager.DisplayName(mod.FileName), (mod.ModId ?? "?") + "  ·  " + (mod.Version ?? "?"), mod.Enabled, sizeBytes, ToggleMod);
			}).ToList();
			RunOnUi(() =>
			{
				_allMods.Clear();
				_allMods.AddRange(modRows);
				ApplyModFilter();
				EnrichModsAsync(scanSnapshot, modsDir, instance.McVersion);
				Worlds.Clear();
				foreach (SimpleRow item3 in worldRows)
				{
					Worlds.Add(item3);
				}
				Backups.Clear();
				foreach (BackupInfo item4 in backups)
				{
					Backups.Add(new SimpleRow(item4.WorldName, $"{item4.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {item4.Megabytes} MB", item4.FileName, item4.FileCount + " files", item4));
				}
			});
		});
	}

	public void CycleModsSort()
	{
		_modsSort = (_modsSort + 1) % 4;
		Raise("ModsSortLabel");
		ApplyModFilter();
	}

	private void ApplyModFilter()
	{
		IEnumerable<ModRowVm> enumerable = _allMods.Where((ModRowVm m) => m.Matches(_modFilter));
		string modsState = _modsState;
		IEnumerable<ModRowVm> enumerable2;
		if (modsState == "Enabled")
		{
			enumerable2 = enumerable.Where((ModRowVm m) => m.IsEnabled);
		}
		else
		{
			enumerable2 = ((!(modsState == "Disabled")) ? enumerable : enumerable.Where((ModRowVm m) => !m.IsEnabled));
		}
		enumerable = enumerable2;
		enumerable = _modsSort switch
		{
			1 => (IEnumerable<ModRowVm>)enumerable.OrderByDescending((ModRowVm m) => m.DisplayName, StringComparer.CurrentCultureIgnoreCase), 
			2 => enumerable.OrderBy((ModRowVm m) => m.SizeBytes), 
			3 => enumerable.OrderByDescending((ModRowVm m) => m.SizeBytes), 
			_ => enumerable.OrderBy((ModRowVm m) => m.DisplayName, StringComparer.CurrentCultureIgnoreCase), 
		};
		Mods.Clear();
		foreach (ModRowVm item in enumerable)
		{
			Mods.Add(item);
		}
		Raise("ModsSummary");
		Raise("ModsShownLabel");
	}

	private void ToggleMod(ModRowVm row, bool enable)
	{
		InstanceVm selected = Selected;
		if (selected == null)
		{
			return;
		}
		if (!ModManager.SetEnabled(Path.Combine(selected.GameDirectory, "mods"), row.FileName, enable))
		{
			row.SetEnabledSilently(!enable);
			AppendConsole("ERROR", "could not " + (enable ? "enable " : "disable") + " " + row.FileName);
		}
		else
		{
			string file = (enable ? ModManager.DisplayName(row.FileName) : (row.FileName + ".disabled"));
			if (!row.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && !enable)
			{
				file = row.FileName + ".disabled";
			}
			row.SetFile(file);
			row.SetEnabledSilently(enable);
			AppendConsole("OK", (enable ? "enabled " : "disabled ") + ModManager.DisplayName(row.FileName));
			Raise("ModsSummary");
			if (_modsState != "All")
			{
				ApplyModFilter();
			}
		}
		FlushConsole();
	}

	public async Task DeleteModAsync(ModRowVm row)
	{
		InstanceVm? selected = Selected;
		if (selected == null || row == null)
		{
			return;
		}
		string clean = ModManager.DisplayName(row.FileName);
		if (!(await ConfirmAsync("Remove mod \"" + row.DisplayName + "\"?", $"File '{row.FileName}' will be permanently deleted from {Path.Combine(selected.GameDirectory, "mods")}. Tip: use the toggle switch if you only want to disable it temporarily.", "DELETE MOD")))
		{
			return;
		}
		string modsDir = Path.Combine(selected.GameDirectory, "mods");
		if (ModManager.Delete(modsDir, row.FileName))
		{
			_allMods.Remove(row);
			Mods.Remove(row);
			PendingModUpdatesCount = Enumerable.Count(_allMods, (ModRowVm m) => m.HasUpdate);
			AppendConsole("OK", "deleted mod: " + clean);
			FlushConsole();
			Raise("ModsSummary");
			Raise("ModsShownLabel");
			await RefreshAsync();
		}
		else
		{
			AppendConsole("ERROR", "could not delete " + row.FileName + " (file may be locked by Java)");
			FlushConsole();
		}
	}

	public async Task InstallDroppedJarsAsync(string[] filePaths)
	{
		InstanceVm? selected = Selected;
		if (selected == null || filePaths == null || filePaths.Length == 0)
		{
			return;
		}
		string modsDir = Path.Combine(selected.GameDirectory, "mods");
		var (added, replaced, skipped) = await Task.Run(() => ModManager.InstallLocalJars(modsDir, filePaths));
		AppendConsole((added + replaced > 0) ? "OK" : "WARN", $"mods import: {added} added, {replaced} replaced, {skipped} skipped");
		FlushConsole();
		await RefreshAsync();
		if (Selected != null)
		{
			await LoadInstanceDetailsAsync(Selected);
		}
	}

	public async Task CheckModUpdatesAsync()
	{
		InstanceVm? selected = Selected;
		if (selected == null || IsModUpdateBusy)
		{
			return;
		}
		IsModUpdateBusy = true;
		ModsMetaStatus = "checking Modrinth for updates…";
		try
		{
			string modsDir = Path.Combine(selected.GameDirectory, "mods");
			string mc = selected.McVersion;
			string loader = ModStore.LoaderOf(selected.VersionId);
			Progress<string> log = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "updates: " + line);
			});
			Dictionary<string, ModUpdateCandidate> found = await Task.Run(() => ModUpdater.CheckUpdatesAsync(modsDir, mc, loader, log).GetAwaiter().GetResult());
			RunOnUi(() =>
			{
				int matched = 0;
				foreach (ModRowVm mod in _allMods)
				{
					if (found.TryGetValue(mod.FileName, out ModUpdateCandidate? cand))
					{
						mod.SetUpdate(cand);
						matched++;
					}
					else
					{
						mod.SetUpdate(null);
					}
				}
				PendingModUpdatesCount = matched;
				ModsMetaStatus = ((matched == 0) ? "all mods up to date" : $"{matched} update(s) ready");
				FlushConsole();
			});
		}
		catch (Exception ex)
		{
			ModsMetaStatus = "update check failed";
			AppendConsole("ERROR", "updates: " + ex.Message);
			FlushConsole();
		}
		finally
		{
			IsModUpdateBusy = false;
		}
	}

	public async Task UpdateSingleModAsync(ModRowVm row)
	{
		InstanceVm? selected = Selected;
		ModUpdateCandidate? cand = row?.PendingUpdate;
		if (selected == null || row == null || cand == null || row.IsUpdating)
		{
			return;
		}
		row.IsUpdating = true;
		string modsDir = Path.Combine(selected.GameDirectory, "mods");
		try
		{
			string newFile = await Task.Run(() => ModUpdater.ApplyUpdateAsync(modsDir, cand).GetAwaiter().GetResult());
			long newSize = 0L;
			try
			{
				newSize = new FileInfo(Path.Combine(modsDir, newFile)).Length;
			}
			catch
			{
			}
			row.SetFile(newFile, newSize);
			row.SetUpdate(null);
			PendingModUpdatesCount = Enumerable.Count(_allMods, (ModRowVm m) => m.HasUpdate);
			ModsMetaStatus = ((PendingModUpdatesCount == 0) ? "all mods updated" : $"{PendingModUpdatesCount} update(s) remaining");
			AppendConsole("OK", $"updated {row.DisplayName} → {newFile}");
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", $"update failed for {row.DisplayName}: {ex.Message}");
		}
		finally
		{
			row.IsUpdating = false;
			FlushConsole();
		}
	}

	public async Task UpdateAllModsAsync()
	{
		InstanceVm? selected = Selected;
		if (selected == null || IsModUpdateBusy)
		{
			return;
		}
		List<ModRowVm> targets = _allMods.Where((ModRowVm m) => m.HasUpdate && !m.IsUpdating).ToList();
		if (targets.Count == 0)
		{
			return;
		}
		IsModUpdateBusy = true;
		try
		{
			int done = 0;
			foreach (ModRowVm row in targets)
			{
				done++;
				ModsMetaStatus = $"updating {done}/{targets.Count}: {row.DisplayName}…";
				await UpdateSingleModAsync(row);
			}
			await RefreshAsync();
			if (Selected != null)
			{
				await LoadInstanceDetailsAsync(Selected);
			}
			ModsMetaStatus = "all updates installed";
		}
		finally
		{
			IsModUpdateBusy = false;
		}
	}

	private async Task EnrichModsAsync(IReadOnlyList<ModEntry> scanned, string modsDir, string mcVersion)
	{
		if (scanned.Count == 0)
		{
			return;
		}
		try
		{
			ModMetaStore store = ModMetaStore.Load(Paths, Settings.CurseForgeKey);
			int withIcon = 0;
			RunOnUi(Apply);
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "covers: " + line);
			});
			if (await Task.Run(() => store.EnrichAsync(scanned, modsDir, mcVersion, progress, () =>
			{
				RunOnUi(Apply);
			})) > 0)
			{
				RunOnUi(Apply);
				FlushConsole();
			}
			void Apply()
			{
				withIcon = 0;
				foreach (ModRowVm allMod in _allMods)
				{
					ModMeta modMeta = store.Get(allMod.FileName);
					if (modMeta != null)
					{
						allMod.ApplyMeta(modMeta.Title, modMeta.Description, store.IconPath(modMeta));
						if (allMod.HasIcon)
						{
							withIcon++;
						}
					}
				}
				ModsMetaStatus = ((withIcon == 0) ? "" : $"{withIcon}/{_allMods.Count} with cover");
				Raise("ModsMetaStatus");
			}
		}
		catch (Exception ex)
		{
			Log("mod covers failed: " + ex);
		}
	}

	public Task<bool> ConfirmAsync(string title, string body, string yesLabel = "DELETE")
	{
		ConfirmTitle = title;
		ConfirmBody = body;
		ConfirmYesLabel = yesLabel;
		ConfirmVisible = true;
		_confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		return _confirm.Task;
	}

	private void ResolveConfirm(bool answer)
	{
		ConfirmVisible = false;
		_confirm?.TrySetResult(answer);
		_confirm = null;
	}

	public void RefreshInstancePicker()
	{
		InstancePicker.Clear();
		string b = Selected?.Id;
		foreach (InstanceVm instance in Instances)
		{
			InstancePicker.Add(new InstancePickVm(instance, string.Equals(instance.Id, b, StringComparison.OrdinalIgnoreCase)));
		}
		Raise("InstancePickerSummary");
	}

	public void PickInstance(InstancePickVm row)
	{
		Selected = row.Model;
		RefreshInstancePicker();
		AppendConsole("INFO", "active instance: " + row.Model.Name);
		FlushConsole();
		Navigate?.Invoke("Home");
	}

	public void OpenVersionFolder(VersionRowVm row)
	{
		string text = Paths.VersionDir(row.Id);
		try
		{
			if (Directory.Exists(text))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + text + "\"")
				{
					UseShellExecute = true
				});
				return;
			}
			Directory.CreateDirectory(Paths.Versions);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.Versions + "\"")
			{
				UseShellExecute = true
			});
			AppendConsole("WARN", row.Id + " is not downloaded yet - opened the versions folder instead");
			FlushConsole();
		}
		catch (Exception ex)
		{
			AppendConsole("WARN", "could not open the folder: " + ex.Message);
			FlushConsole();
		}
	}

	public int InstancesUsingVersion(string versionId)
	{
		return Enumerable.Count(Instances, (InstanceVm i) => i.VersionId == versionId || i.VersionId.StartsWith(versionId + "-forge-", StringComparison.OrdinalIgnoreCase));
	}

	public async Task DeleteVersionAsync(VersionRowVm row)
	{
		if (!row.Installed)
		{
			return;
		}
		int users = InstancesUsingVersion(row.Id);
		string vDir = Paths.VersionDir(row.Id);
		string body = ((users == 0) ? ("Only " + vDir + " is removed. Assets, libraries and your mod packs stay untouched.") : $"{users} instance(s) use this version and will not launch until you download it again. Only {vDir} is removed — your pack folders stay untouched.");
		if (!(await ConfirmAsync("Delete version " + row.Id + "?", body, "DELETE VERSION")))
		{
			return;
		}
		string dir = Paths.VersionDir(row.Id);
		int failures = 0;
		await Task.Run(() =>
		{
			try
			{
				if (Directory.Exists(dir))
				{
					Directory.Delete(dir, recursive: true);
				}
			}
			catch
			{
				failures++;
			}
		});
		row.Installed = !Directory.Exists(dir);
		if (row.Installed)
		{
			AppendConsole("WARN", "could not delete " + dir + " - a file may be open in another program");
		}
		else
		{
			AppendConsole("OK", "deleted version " + row.Id + " (" + users + " instance(s) will re-download it if needed)");
		}
		FlushConsole();
		VersionRowVm versionRowVm = _allVersions.FirstOrDefault((VersionRowVm v) => v.Id == row.Id);
		if (versionRowVm != null)
		{
			versionRowVm.Installed = row.Installed;
		}
		Raise("VersionsSummary");
		Raise("VersionsShown");
	}

	public async void DeleteSelectedInstance()
	{
		if (Selected == null)
		{
			return;
		}
		InstanceVm instance = Selected;
		string body = (instance.GameDirectory.StartsWith(Paths.Instances, StringComparison.OrdinalIgnoreCase) ? ("Its folder (" + instance.GameDirectory + ") is removed, including the worlds and mods inside it.") : ("Its game folder (" + instance.GameDirectory + ") is outside " + Paths.Instances + ", so Nova only removes the instance entry and leaves every file where it is."));
		if (await ConfirmAsync("Delete instance \"" + instance.Name + "\"?", body, "DELETE INSTANCE"))
		{
			try
			{
				bool flag = new InstanceStore(Paths).Remove(instance.Id);
				AppendConsole("OK", flag ? ("instance deleted: " + instance.Name) : ("instance entry deleted, folder kept: " + instance.GameDirectory));
				FlushConsole();
			}
			catch (Exception ex)
			{
				AppendConsole("WARN", "could not delete the instance: " + ex.Message);
				FlushConsole();
			}
			Selected = null;
			RefreshAsync();
		}
	}

	private NovaAccount ActiveAccount()
	{
		return _accountStore.Active(Settings);
	}

	public void RefreshAccounts()
	{
		Accounts.Clear();
		NovaAccount novaAccount = ActiveAccount();
		foreach (NovaAccount item in _accountStore.All())
		{
			Accounts.Add(new AccountRowVm(item)
			{
				IsActive = (item.Id == novaAccount.Id)
			});
		}
		if (Accounts.Count == 0)
		{
			Accounts.Add(new AccountRowVm(novaAccount)
			{
				IsActive = true
			});
		}
		OfflineName = novaAccount.Name;
		if (string.IsNullOrWhiteSpace(_microsoftStatus))
		{
			MicrosoftStatus = (MicrosoftAuth.LooksLikeClientId(Settings.MicrosoftClientId) ? "ready - press sign in and approve the code in your browser" : "Microsoft sign-in is off until a client id is saved in Settings");
		}
		Raise("AccountsSummary");
		Raise("ActiveAccountLabel");
		Raise("HeaderAccount");
	}

	private void AddOfflineAccount()
	{
		try
		{
			NovaAccount novaAccount = NovaAccount.CreateOffline(OfflineName);
			if (_accountStore.Find(novaAccount.Id) != null)
			{
				throw new ArgumentException("That offline account already exists. Select it from the account list.");
			}
			_accountStore.Save(novaAccount);
			Settings.ActiveAccountId = novaAccount.Id;
			Settings.Save(Paths);
			AppendConsole("OK", "account: offline player " + novaAccount.Name + " is now active");
			FlushConsole();
			RefreshAccounts();
		}
		catch (Exception ex)
		{
			MicrosoftStatus = ex.Message;
		}
	}

	public void UseAccount(AccountRowVm row)
	{
		Settings.ActiveAccountId = row.Model.Id;
		Settings.Save(Paths);
		AppendConsole("OK", "account: switched to " + row.Name + " (" + row.KindLabel + ")");
		FlushConsole();
		RefreshAccounts();
	}

	public void RemoveAccount(AccountRowVm row)
	{
		_accountStore.Remove(row.Model.Id);
		if (string.Equals(Settings.ActiveAccountId, row.Model.Id, StringComparison.OrdinalIgnoreCase))
		{
			Settings.ActiveAccountId = null;
			Settings.Save(Paths);
		}
		AppendConsole("INFO", "account removed: " + row.Name);
		FlushConsole();
		RefreshAccounts();
	}

	private async void SignInWithMicrosoft()
	{
		if (_isMicrosoftSigningIn)
		{
			return;
		}
		string clientId = Settings.MicrosoftClientId;
		if (!MicrosoftAuth.LooksLikeClientId(clientId))
		{
			MicrosoftStatus = "Microsoft sign-in needs a client id first - open Settings and paste your Azure app id (GUID).";
			AppendConsole("WARN", "microsoft: no client id configured (Settings → Microsoft sign-in)");
			FlushConsole();
			return;
		}
		_isMicrosoftSigningIn = true;
		_signInCts = new CancellationTokenSource();
		Raise("CanSignInMicrosoft");
		DeviceCode = "";
		MicrosoftStatus = "contacting Microsoft…";
		try
		{
			using MicrosoftAuth auth = new MicrosoftAuth();
			DeviceCodeInfo deviceCodeInfo = await auth.StartDeviceCodeAsync(clientId, _signInCts.Token);
			DeviceCode = deviceCodeInfo.UserCode;
			DeviceLink = deviceCodeInfo.VerificationUri;
			MicrosoftStatus = "waiting for you to approve the code in the browser…";
			AppendConsole("INFO", "microsoft: open " + deviceCodeInfo.VerificationUri + " and enter " + deviceCodeInfo.UserCode);
			FlushConsole();
			Progress<string> log = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			NovaAccount novaAccount = await auth.WaitForSignInAsync(clientId, deviceCodeInfo, log, _signInCts.Token);
			if (novaAccount == null)
			{
				MicrosoftStatus = "sign-in did not complete (code expired or declined).";
				return;
			}
			_accountStore.Save(novaAccount);
			Settings.ActiveAccountId = novaAccount.Id;
			Settings.Save(Paths);
			MicrosoftStatus = "signed in as " + novaAccount.Name + " - online play is ready";
			AppendConsole("OK", "microsoft: signed in as " + novaAccount.Name);
			FlushConsole();
			RefreshAccounts();
		}
		catch (Exception ex)
		{
			MicrosoftStatus = "sign-in failed: " + ex.Message;
			Log("microsoft sign-in failed: " + ex);
		}
		finally
		{
			DeviceCode = "";
			_isMicrosoftSigningIn = false;
			Raise("CanSignInMicrosoft");
		}
	}

	public async Task LoadVersionsAsync(bool force = false)
	{
		if (IsVersionBusy || (_versionsLoaded && !force))
		{
			return;
		}
		IsVersionBusy = true;
		VersionStatus = "talking to Mojang + Forge…";
		try
		{
			List<MinecraftVersion> list = await Task.Run(() => VersionLibrary.GetMinecraftVersionsAsync(Paths).GetAwaiter().GetResult());
			RunOnUi(() =>
			{
				_allVersions.Clear();
				foreach (MinecraftVersion item in list)
				{
					_allVersions.Add(new VersionRowVm(item));
				}
				_versionsLoaded = true;
				ApplyVersionFilter();
				int value = Enumerable.Count(_allVersions, (VersionRowVm v) => v.HasForge);
				VersionStatus = $"{_allVersions.Count} releases from 1.8 onward · {value} with Forge builds";
			});
		}
		catch (Exception ex)
		{
			VersionStatus = "could not load the version list: " + ex.Message;
			Log("versions failed: " + ex);
		}
		finally
		{
			IsVersionBusy = false;
		}
	}

	public Task SetVersionsModeAsync(string mode)
	{
		_versionsMode = mode;
		Raise("VersionsModeLabel");
		ApplyVersionFilter();
		return Task.CompletedTask;
	}

	private void ApplyVersionFilter()
	{
		Versions.Clear();
		IEnumerable<VersionRowVm> enumerable = _allVersions.Where((VersionRowVm v) => v.Matches(_versionFilter));
		enumerable = _versionsMode switch
		{
			"Forge" => enumerable.Where((VersionRowVm v) => v.HasForge),
			"Fabric" => enumerable.Where((VersionRowVm v) => v.HasFabric),
			"NeoForge" => enumerable.Where((VersionRowVm v) => v.HasNeoForge),
			_ => enumerable
		};
		foreach (VersionRowVm item in enumerable)
		{
			Versions.Add(item);
		}
		Raise("VersionsSummary");
		Raise("VersionsShown");
	}

	public Task InstallVersionAsync(VersionRowVm row, bool useForge)
	{
		return InstallVersionAsync(row, useForge ? "forge" : "vanilla");
	}

	public async Task InstallVersionAsync(VersionRowVm row, string loaderKind)
	{
		if (IsVersionBusy)
		{
			return;
		}
		string mc = row.Id;
		string kind = (loaderKind ?? "vanilla").ToLowerInvariant();
		string versionId;
		string name;
		bool isModded = kind != "vanilla";
		if (kind == "forge")
		{
			string? text = row.ForgeRecommended ?? row.ForgeLatest;
			if (text == null)
			{
				VersionStatus = "no Forge build is published for " + mc;
				return;
			}
			versionId = mc + "-forge-" + text;
			name = mc + " Forge " + text;
		}
		else if (kind == "fabric")
		{
			if (!row.HasFabric)
			{
				VersionStatus = "no Fabric build is published for " + mc;
				return;
			}
			versionId = "fabric-loader-latest-" + mc;
			name = mc + " Fabric";
		}
		else if (kind == "neoforge")
		{
			if (!row.HasNeoForge || string.IsNullOrEmpty(row.NeoForgeLatest))
			{
				VersionStatus = "no NeoForge build is published for " + mc;
				return;
			}
			versionId = "neoforge-" + row.NeoForgeLatest;
			name = mc + " NeoForge " + row.NeoForgeLatest;
		}
		else
		{
			versionId = mc;
			name = "Minecraft " + mc;
		}
		IsVersionBusy = true;
		row.IsBusy = true;
		VersionActiveLabel = "INSTALLING " + name.ToUpperInvariant();
		VersionProgress = 0.02;
		row.UpdateProgress("Preparing", 0.02, "connecting to repositories…");
		VersionStatus = "preparing " + name + "…";
		StatusText = "Installing " + name;
		StatusDetail = "Fetching version metadata…";
		StatusProgress = 0.02;
		StatusIndeterminate = false;
		AppendConsole("INFO", "versions: installing " + name);
		FlushConsole();
		try
		{
			IProgress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "  " + line);
			});
			IProgress<DownloadProgress> step1Progress = new Progress<DownloadProgress>(delegate(DownloadProgress dp)
			{
				double weighted = 0.02 + dp.Fraction * (isModded ? 0.43 : 0.33);
				RunOnUi(delegate
				{
					row.UpdateProgress(dp.Stage, weighted, dp.Detail);
					VersionProgress = weighted;
					VersionStatus = $"{name}  ·  {dp.Stage}  ·  {dp.Detail}";
					StatusProgress = weighted;
					StatusDetail = dp.Detail;
				});
			});
			int memory = 4096;
			InstanceConfig instance = await Task.Run(() => InstanceFactory.CreateAsync(Paths, name, versionId, memory, progress, default, step1Progress).GetAwaiter().GetResult());
			string finalVerId = instance.VersionId;
			int javaMajor = VersionResolver.GuessJavaMajor(mc);
			RunOnUi(delegate
			{
				double jPct = (isModded ? 0.48 : 0.38);
				row.UpdateProgress($"Java {javaMajor}", jPct, $"verifying Java {javaMajor} runtime…");
				VersionProgress = jPct;
				VersionStatus = $"{finalVerId}  ·  verifying Java {javaMajor} runtime…";
				StatusProgress = jPct;
				StatusDetail = $"Verifying Java {javaMajor} runtime…";
			});
			JavaInstall? javaInstall = await Task.Run(() => JavaProvisioner.EnsureAsync(Paths, javaMajor, progress).GetAwaiter().GetResult());
			if ((object?)javaInstall != null)
			{
				instance.JavaPath = javaInstall.Path;
				new InstanceStore(Paths).Save(instance);
			}
			RunOnUi(delegate
			{
				row.Installed = true;
				VersionStatus = finalVerId + "  ·  syncing libraries & game assets…";
				AppendConsole("OK", "instance created: " + instance.Name + "  (" + instance.Id + ")");
				FlushConsole();
			});
			await RefreshAsync();
			InstanceVm? instanceVm = Instances.FirstOrDefault((InstanceVm i) => i.Id.Equals(instance.Id, StringComparison.OrdinalIgnoreCase));
			if (instanceVm != null)
			{
				Selected = instanceVm;
			}
			double repairBase = (isModded ? 0.50 : 0.40);
			Progress<string> repairLog = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "  " + line);
			});
			IProgress<DownloadProgress> step2Progress = new Progress<DownloadProgress>(delegate(DownloadProgress dp)
			{
				double span = 1.0 - repairBase;
				double sub = dp.Stage switch
				{
					"Client JAR" => dp.Fraction * 0.15,
					"Libraries" => 0.15 + dp.Fraction * 0.35,
					"Assets" => 0.50 + dp.Fraction * 0.46,
					"Natives" => 0.97,
					"Ready" => 1.0,
					_ => dp.Fraction * 0.5
				};
				double overall = Math.Clamp(repairBase + sub * span, repairBase, 1.0);
				RunOnUi(delegate
				{
					row.UpdateProgress(dp.Stage, overall, dp.Detail);
					VersionProgress = overall;
					VersionStatus = $"{finalVerId}  ·  {dp.Stage}  ·  {dp.Detail}";
					StatusText = "Installing " + finalVerId;
					StatusProgress = overall;
					StatusDetail = dp.Detail;
				});
			});
			await Task.Run(() => Doctor.RepairAsync(instance, Paths, repairLog, default, step2Progress).GetAwaiter().GetResult());
			RunOnUi(delegate
			{
				row.UpdateProgress("Installed", 1.0, "ready to play");
				VersionProgress = 1.0;
				VersionStatus = finalVerId + " is ready — instance “" + instance.Name + "” selected";
				StatusText = "Ready";
				StatusProgress = 1.0;
				StatusDetail = finalVerId + " installed and verified. Press LAUNCH.";
				AppendConsole("OK", finalVerId + " ready to play");
				FlushConsole();
			});
		}
		catch (Exception ex)
		{
			Exception ex3 = ex;
			RunOnUi(delegate
			{
				row.UpdateProgress("Failed", 0.0, ex3.Message);
				VersionProgress = 0.0;
				VersionStatus = "install failed: " + ex3.Message;
				StatusText = "Install failed";
				StatusDetail = ex3.Message;
				AppendConsole("ERROR", "versions: " + ex3.Message);
				FlushConsole();
			});
			Log("version install failed: " + ex3);
		}
		finally
		{
			row.IsBusy = false;
			IsVersionBusy = false;
			VersionActiveLabel = "";
			Raise("VersionsSummary");
		}
	}

	public async Task DuplicateSelectedAsync()
	{
		if (Selected == null || IsVersionBusy)
		{
			return;
		}
		InstanceConfig source = Selected.Config;
		string name = source.Name + " copy";
		IsVersionBusy = true;
		AppendConsole("INFO", "duplicating " + source.Name + " -> " + name);
		FlushConsole();
		try
		{
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "  " + line);
			});
			InstanceConfig clone = await Task.Run(() => InstanceFactory.DuplicateAsync(Paths, source, name, copyGameFiles: true, progress).GetAwaiter().GetResult());
			await RefreshAsync();
			InstanceVm instanceVm = Instances.FirstOrDefault((InstanceVm i) => i.Id.Equals(clone.Id, StringComparison.OrdinalIgnoreCase));
			if (instanceVm != null)
			{
				Selected = instanceVm;
			}
			AppendConsole("OK", "copy created: " + clone.Name + "  (" + clone.GameDirectory + ")");
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", "duplicate failed: " + ex.Message);
			Log("duplicate failed: " + ex);
		}
		finally
		{
			FlushConsole();
			IsVersionBusy = false;
		}
	}

	private void RaiseModsTargetState()
	{
		Raise("ModsTargetVersionId");
		Raise("ModsTargetIsAuto");
		Raise("ModsTargetButtonLabel");
		Raise("ModsBrowseTargetLine");
	}

	private static string ShortVersion(string versionId)
	{
		if (string.IsNullOrWhiteSpace(versionId))
		{
			return "";
		}
		string mc = ModStore.McVersionOf(versionId);
		string loader = ModStore.FormatLoaderLabel(versionId);
		if (loader == "Vanilla")
		{
			return mc;
		}
		return mc + " · " + loader;
	}

	public void RefreshForgeTargets()
	{
		ForgeTargets.Clear();
		List<string> list = new List<string>();
		foreach (InstanceVm instance in Instances)
		{
			if (ModStore.LoaderOf(instance.VersionId) != "vanilla" && !list.Contains(instance.VersionId))
			{
				list.Add(instance.VersionId);
			}
		}
		try
		{
			if (Directory.Exists(Paths.Versions))
			{
				string[] directories = Directory.GetDirectories(Paths.Versions);
				for (int i = 0; i < directories.Length; i++)
				{
					string fileName = Path.GetFileName(directories[i]);
					if (ModStore.LoaderOf(fileName) != "vanilla" && !list.Contains(fileName))
					{
						list.Add(fileName);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log("could not list versions: " + ex.Message);
		}
		string modsTargetVersionId = ModsTargetVersionId;
		foreach (string id in list)
		{
			InstanceVm? instanceVm = Instances.FirstOrDefault((InstanceVm instanceVm2) => string.Equals(instanceVm2.VersionId, id, StringComparison.OrdinalIgnoreCase));
			ForgeTargets.Add(new ForgeTargetVm(id, ShortVersion(id), (instanceVm == null) ? "no instance - created on the first download" : ("into " + instanceVm.Name), string.Equals(id, modsTargetVersionId, StringComparison.OrdinalIgnoreCase)));
		}
		if (ForgeTargets.Count == 0)
		{
			ForgeTargets.Add(new ForgeTargetVm("", "nothing yet", "install Forge, Fabric, or NeoForge from the Versions page", isSelected: true));
		}
	}

	public void PickModsTarget(ForgeTargetVm row)
	{
		ModsTargetPickerVisible = false;
		if (!string.IsNullOrWhiteSpace(row.VersionId))
		{
			ModsTargetVersion = row.VersionId;
			_modsBrowseLoaded = true;
			AppendConsole("INFO", "mods: working with " + ShortVersion(row.VersionId));
			FlushConsole();
			RefreshForgeTargets();
			SearchModBrowseAsync();
		}
	}

	private static string NormalizeToken(string value)
	{
		return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
	}

	private void RefreshInstalledModTokens()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		InstanceVm modsTargetInstance = ModsTargetInstance;
		if (modsTargetInstance != null)
		{
			string path = Path.Combine(modsTargetInstance.GameDirectory, "mods");
			try
			{
				if (Directory.Exists(path))
				{
					string[] files = Directory.GetFiles(path, "*.jar*");
					for (int i = 0; i < files.Length; i++)
					{
						string fileName = Path.GetFileName(files[i]);
						string text;
						if (!fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
						{
							text = fileName;
						}
						else
						{
							string text2 = fileName;
							int length = ".disabled".Length;
							text = text2.Substring(0, text2.Length - length);
						}
						string path2 = text;
						path2 = Path.GetFileNameWithoutExtension(path2);
						string value = path2.Split(new char[4] { '-', '_', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
						if (!string.IsNullOrWhiteSpace(value))
						{
							hashSet.Add(NormalizeToken(value));
						}
						hashSet.Add(NormalizeToken(path2));
					}
				}
			}
			catch (Exception ex)
			{
				Log("could not read the mods folder: " + ex.Message);
			}
		}
		_installedModTokens = hashSet;
	}

	private void ApplyInstalledFlags()
	{
		foreach (StoreItemVm modBrowseItem in ModBrowseItems)
		{
			string text = NormalizeToken(modBrowseItem.Model.Slug);
			string name = NormalizeToken(modBrowseItem.Model.Name);
			bool isInstalled = (text.Length >= 3 && _installedModTokens.Contains(text)) || (name.Length >= 5 && _installedModTokens.Any((string token) => token.Contains(name)));
			modBrowseItem.IsInstalled = isInstalled;
		}
	}

	public async Task SearchModBrowseAsync(bool append = false)
	{
		if (_modsBrowseBusy)
		{
			return;
		}
		string versionId = ModsTargetVersionId;
		if (string.IsNullOrWhiteSpace(versionId))
		{
			ModsBrowseStatus = "there is no Forge version here yet - download one from the Versions page";
			return;
		}
		StoreSource source = (StoreSourceIsCurse ? StoreSource.CurseForge : StoreSource.Modrinth);
		if (source == StoreSource.CurseForge && string.IsNullOrWhiteSpace(Settings.CurseForgeKey))
		{
			ModsBrowseStatus = "CurseForge needs a free API key (Settings), or switch the store to Modrinth";
			return;
		}
		string mc = ModStore.McVersionOf(versionId);
		string loader = ModStore.LoaderOf(versionId);
		if (loader == "vanilla")
		{
			loader = "forge";
		}
		string query = ModsBrowseQuery;
		string key = Settings.CurseForgeKey;
		_modsBrowseBusy = true;
		ModsBrowseStatus = "searching…";
		try
		{
			RefreshInstalledModTokens();
			List<StoreMod> found = await Task.Run(() =>
			{
				using ModStore modStore = new ModStore(key);
				return modStore.SearchAsync(source, query, mc, loader, 24).GetAwaiter().GetResult();
			});
			RunOnUi(() =>
			{
				ModBrowseItems.Clear();
				foreach (StoreMod item in found)
				{
					ModBrowseItems.Add(new StoreItemVm(item));
				}
				ApplyInstalledFlags();
				LoadModBrowseIconsAsync();
				int value = Enumerable.Count(ModBrowseItems, (StoreItemVm item) => item.IsInstalled);
				ModsBrowseStatus = ((ModBrowseItems.Count == 0) ? $"no mods for {mc} {loader} matched ‘{query}’" : $"{ModBrowseItems.Count} mods for {mc} {loader}  ·  {value} already in {ShortVersion(versionId)}");
			});
		}
		catch (Exception ex)
		{
			ModsBrowseStatus = "search failed: " + ex.Message;
			Log("mods browse search failed: " + ex);
		}
		finally
		{
			_modsBrowseBusy = false;
		}
	}

	private async Task LoadModBrowseIconsAsync()
	{
		if (_browseIconsLoading)
		{
			return;
		}
		_browseIconsLoading = true;
		try
		{
			List<StoreItemVm> list = ModBrowseItems.Where((StoreItemVm item) => !item.HasIcon && !string.IsNullOrWhiteSpace(item.Model.IconUrl)).ToList();
			if (list.Count == 0)
			{
				return;
			}
			string cacheDir = Path.Combine(Paths.Root, "cache", "store-icons");
			SemaphoreSlim throttle = new SemaphoreSlim(4);
			try
			{
				await Task.WhenAll(((IEnumerable<StoreItemVm>)list).Select((Func<StoreItemVm, Task>)(async (StoreItemVm item) =>
				{
					await throttle.WaitAsync();
					try
					{
						string path = await IconCache.FetchAsync(item.Model.IconUrl, cacheDir, item.Model.Slug);
						if (path != null)
						{
							RunOnUi(() =>
							{
								item.SetIcon(path);
							});
						}
					}
					finally
					{
						throttle.Release();
					}
				})));
			}
			finally
			{
				if (throttle != null)
				{
					((IDisposable)throttle).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Log("mod browse icons failed: " + ex);
		}
		finally
		{
			_browseIconsLoading = false;
		}
	}

	private async Task<InstanceVm?> EnsureInstanceForVersionAsync(string versionId, IProgress<string> progress)
	{
		InstanceVm instanceVm = Instances.FirstOrDefault((InstanceVm i) => string.Equals(i.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
		if (instanceVm != null)
		{
			return instanceVm;
		}
		string mc = ModStore.McVersionOf(versionId);
		string name = ShortVersion(versionId).Replace(" · Forge ", " Forge ");
		AppendConsole("INFO", "mods: creating an instance for " + versionId);
		FlushConsole();
		InstanceConfig config = await Task.Run(() => InstanceFactory.CreateAsync(Paths, name, versionId, 4096, progress).GetAwaiter().GetResult());
		JavaInstall javaInstall = await Task.Run(() => JavaProvisioner.EnsureAsync(Paths, VersionResolver.GuessJavaMajor(mc), progress).GetAwaiter().GetResult());
		if ((object)javaInstall != null)
		{
			config.JavaPath = javaInstall.Path;
			new InstanceStore(Paths).Save(config);
		}
		await RefreshAsync();
		RefreshForgeTargets();
		InstanceVm instanceVm2 = Instances.FirstOrDefault((InstanceVm i) => string.Equals(i.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
		if (instanceVm2 == null)
		{
			await RefreshAsync();
			RefreshForgeTargets();
			instanceVm2 = Instances.FirstOrDefault((InstanceVm i) => string.Equals(i.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
		}
		return instanceVm2;
	}

	public async Task DownloadModAsync(StoreItemVm item)
	{
		if (item == null || item.IsBusy || item.IsInstalled)
		{
			return;
		}
		string versionId = ModsTargetVersionId;
		if (string.IsNullOrWhiteSpace(versionId))
		{
			return;
		}
		item.IsBusy = true;
		ModsBrowseStatus = "downloading " + item.Name + "…";
		try
		{
			IProgress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", "mods: " + line);
			});
			InstanceVm instance = await EnsureInstanceForVersionAsync(versionId, progress);
			if (instance == null)
			{
				ModsBrowseStatus = "could not create an instance for " + versionId;
				return;
			}
			ModInstallResult modInstallResult = await Task.Run(() =>
			{
				using ModStore modStore = new ModStore(Settings.CurseForgeKey);
				return modStore.InstallAsync(item.Model, instance.Config, withDependencies: true, progress).GetAwaiter().GetResult();
			});
			bool landed = modInstallResult.Installed.Count > 0 || modInstallResult.Skipped.Count > 0;
			ModsBrowseStatus = item.Name + ": " + modInstallResult.Summary;
			AppendConsole((modInstallResult.Failed.Count == 0) ? "OK" : "WARN", "mods: " + item.Name + " - " + modInstallResult.Summary);
			await RefreshAsync();
			RefreshForgeTargets();
			RefreshInstalledModTokens();
			ApplyInstalledFlags();
			if (landed)
			{
				item.IsInstalled = true;
			}
			if (Selected != null)
			{
				await LoadInstanceDetailsAsync(Selected);
			}
		}
		catch (Exception ex)
		{
			ModsBrowseStatus = "download failed: " + ex.Message;
			AppendConsole("ERROR", "mods: " + ex.Message);
		}
		finally
		{
			item.IsBusy = false;
			FlushConsole();
		}
	}

	public void PersistStoreSource()
	{
		Settings.StoreSource = SourceName;
		Settings.Save(Paths);
	}

	public void RefreshStoreVersionChoices()
	{
		StoreVersionChoices.Clear();
		StoreVersionChoices.Add(new StoreVersionVm("", "AUTO", "follow the active instance", StoreAutoIsOn));
		List<string> list = new List<string>();
		foreach (InstanceVm instance in Instances)
		{
			if (!list.Contains(instance.McVersion))
			{
				list.Add(instance.McVersion);
			}
		}
		string[] popularStoreVersions = PopularStoreVersions;
		foreach (string item in popularStoreVersions)
		{
			if (!list.Contains(item))
			{
				list.Add(item);
			}
		}
		foreach (string version in list)
		{
			bool isSelected = string.Equals(StoreTargetVersion, version, StringComparison.OrdinalIgnoreCase);
			InstanceVm instanceVm = Instances.FirstOrDefault((InstanceVm instanceVm2) => string.Equals(instanceVm2.McVersion, version, StringComparison.OrdinalIgnoreCase));
			string detail = ((instanceVm == null) ? "no instance yet - search only" : ("into " + instanceVm.Name));
			StoreVersionChoices.Add(new StoreVersionVm(version, version, detail, isSelected));
		}
	}

	public void PickStoreTarget(StoreVersionVm row)
	{
		StoreTargetVersion = row.Version;
		StoreTargetPickerVisible = false;
		AppendConsole("INFO", "store: mods for " + (StoreAutoIsOn ? "the active instance" : row.Version));
		FlushConsole();
		RefreshStoreVersionChoices();
		SearchStoreAsync();
	}

	public void CycleStoreSort()
	{
		string storeSort = _storeSort;
		string storeSort2;
		if (storeSort == "downloads")
		{
			storeSort2 = "newest";
		}
		else
		{
			storeSort2 = ((!(storeSort == "newest")) ? "downloads" : "name");
		}
		_storeSort = storeSort2;
		Raise("StoreSortLabel");
		SearchStoreAsync();
	}

	public Task LoadMoreStoreAsync()
	{
		return SearchStoreAsync(append: true);
	}

	public async Task SearchStoreAsync(bool append = false)
	{
		if (IsStoreBusy)
		{
			return;
		}
		if (Selected == null)
		{
			StoreStatus = "select an instance first";
			return;
		}
		StoreSource source = (StoreSourceIsCurse ? StoreSource.CurseForge : StoreSource.Modrinth);
		if (source == StoreSource.CurseForge && string.IsNullOrWhiteSpace(Settings.CurseForgeKey))
		{
			StoreStatus = "CurseForge needs a free API key - add it in Settings, or switch to Modrinth";
			return;
		}
		int offset = (append ? _storeOffset : 0);
		IsStoreBusy = true;
		Raise("CanLoadMore");
		if (!append)
		{
			StoreStatus = "searching…";
		}
		try
		{
			string query = StoreQuery;
			string mc = EffectiveStoreVersion;
			string loader = EffectiveStoreLoader;
			string key = Settings.CurseForgeKey;
			string sort = _storeSort;
			(List<StoreMod> List, string Note) found = await Task.Run(() =>
			{
				using ModStore modStore = new ModStore(key);
				return (List: modStore.SearchAsync(source, query, mc, loader, 20, default, offset, sort).GetAwaiter().GetResult(), Note: modStore.LastSearchNote);
			});
			RunOnUi(() =>
			{
				if (!append)
				{
					StoreItems.Clear();
				}
				foreach (StoreMod mod in found.List)
				{
					if (!append || !StoreItems.Any((StoreItemVm item) => item.Model.Id == mod.Id))
					{
						StoreItems.Add(new StoreItemVm(mod));
					}
				}
				LoadStoreIconsAsync();
				if (offset == 0)
				{
					_storeOffset = 0;
				}
				_storeOffset += found.List.Count;
				HasMoreStore = found.List.Count >= 20;
				string text = ((StoreItems.Count != 0) ? $"{StoreItems.Count} mods for {mc} {loader}  ·  {StoreSortLabel.ToLowerInvariant()}" : (string.IsNullOrWhiteSpace(query) ? $"search {SourceName} for {mc} {loader}" : $"no mods for {mc} {loader} matched ‘{query}’"));
				StoreStatus = (string.IsNullOrWhiteSpace(found.Note) ? text : (text + "  ·  " + found.Note));
			});
		}
		catch (Exception ex)
		{
			StoreStatus = "search failed: " + ex.Message;
			Log("store search failed: " + ex);
		}
		finally
		{
			IsStoreBusy = false;
			Raise("CanLoadMore");
		}
	}

	private async Task LoadStoreIconsAsync()
	{
		if (_iconsLoading)
		{
			return;
		}
		_iconsLoading = true;
		try
		{
			List<StoreItemVm> list = StoreItems.Where((StoreItemVm item) => !item.HasIcon && !string.IsNullOrWhiteSpace(item.Model.IconUrl)).ToList();
			if (list.Count == 0)
			{
				return;
			}
			string cacheDir = Path.Combine(Paths.Root, "cache", "store-icons");
			SemaphoreSlim throttle = new SemaphoreSlim(4);
			try
			{
				await Task.WhenAll(((IEnumerable<StoreItemVm>)list).Select((Func<StoreItemVm, Task>)(async (StoreItemVm item) =>
				{
					await throttle.WaitAsync();
					try
					{
						string path = await IconCache.FetchAsync(item.Model.IconUrl, cacheDir, item.Model.Slug);
						if (path != null)
						{
							RunOnUi(() =>
							{
								item.SetIcon(path);
							});
						}
					}
					finally
					{
						throttle.Release();
					}
				})));
			}
			finally
			{
				if (throttle != null)
				{
					((IDisposable)throttle).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Log("store icons failed: " + ex);
		}
		finally
		{
			_iconsLoading = false;
		}
	}

	public async Task InstallStoreAsync(StoreItemVm item)
	{
		if (item.IsBusy)
		{
			return;
		}
		InstanceVm target = StoreInstallTarget;
		if (target == null || Selected == null)
		{
			StoreStatus = "no instance of " + EffectiveStoreVersion + " yet - create one in Versions, then install";
			AppendConsole("WARN", "store: no instance for " + EffectiveStoreVersion);
			FlushConsole();
			return;
		}
		item.IsBusy = true;
		StoreStatus = "installing " + item.Name + "…";
		AppendConsole("INFO", "store: installing " + item.Name + " into " + target.Name);
		try
		{
			InstanceConfig instance = target.Config;
			string key = Settings.CurseForgeKey;
			IProgress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			ModInstallResult modInstallResult = await Task.Run(() =>
			{
				using ModStore modStore = new ModStore(key);
				return modStore.InstallAsync(item.Model, instance, withDependencies: true, progress).GetAwaiter().GetResult();
			});
			item.IsInstalled = modInstallResult.Installed.Count > 0 || modInstallResult.Skipped.Count > 0;
			StoreStatus = item.Name + ": " + modInstallResult.Summary;
			AppendConsole((modInstallResult.Failed.Count == 0) ? "OK" : "WARN", "store: " + item.Name + " - " + modInstallResult.Summary);
			Log("store install " + item.Name + ": " + modInstallResult.Summary);
			await LoadInstanceDetailsAsync(target);
		}
		catch (Exception ex)
		{
			StoreStatus = "install failed: " + ex.Message;
			AppendConsole("ERROR", ex.Message);
			Log("store install failed: " + ex);
		}
		finally
		{
			item.IsBusy = false;
			FlushConsole();
		}
	}

	public async Task CheckAsync()
	{
		if (Selected == null || IsBusy || IsRunning)
		{
			return;
		}
		IsBusy = true;
		StatusText = "Checking";
		StatusDetail = "Hashing libraries and counting assets…";
		StatusIndeterminate = true;
		AppendConsole("INFO", "health check started for " + Selected.Name);
		Log("health check started for " + Selected.Id);
		try
		{
			InstanceConfig instance = Selected.Config;
			DoctorReport doctorReport = await Task.Run(() => Doctor.RunAsync(instance, Paths, deep: true).GetAwaiter().GetResult());
			if (Selected == null || !string.Equals(Selected.Id, instance.Id, StringComparison.OrdinalIgnoreCase))
			{
				_recheckPending = true;
				Log("health check for " + instance.Id + " finished after the selection changed - re-running");
			}
			else
			{
				Checks.Clear();
				foreach (CheckItem item in doctorReport.Items)
				{
					Checks.Add(new CheckRow(item.Area, item.Status.ToString().ToUpperInvariant(), item.Message, item.Suggestion));
				}
				_checksInstanceId = instance.Id;
				int num = Enumerable.Count(doctorReport.Items, (CheckItem i) => i.Status == CheckStatus.Fail);
				int value = Enumerable.Count(doctorReport.Items, (CheckItem i) => i.Status == CheckStatus.Warn);
				StatusIndeterminate = false;
				StatusProgress = 1.0;
				StatusText = ((num == 0) ? "Healthy" : (num + " problem(s)"));
				StatusDetail = ((num == 0) ? $"{doctorReport.Items.Count} checks passed, {value} warning(s)." : "Press REPAIR to download what is missing.");
				AppendConsole((num == 0) ? "OK" : "ERROR", $"health check: {doctorReport.Items.Count} checks, {num} failed, {value} warnings");
			}
		}
		catch (Exception ex)
		{
			StatusIndeterminate = false;
			StatusText = "Check failed";
			StatusDetail = ex.Message;
			AppendConsole("ERROR", ex.Message);
			Log("check failed: " + ex);
		}
		finally
		{
			IsBusy = false;
		}
		if (_recheckPending)
		{
			_recheckPending = false;
			if (!IsRunning)
			{
				RequestAutoCheck();
			}
		}
	}

	public async Task RepairAsync()
	{
		if (Selected == null || IsBusy || IsRunning)
		{
			return;
		}
		IsBusy = true;
		StatusText = "Repairing";
		StatusDetail = "Downloading missing libraries and assets…";
		StatusProgress = 0.04;
		StatusIndeterminate = false;
		AppendConsole("INFO", "repair started");
		try
		{
			InstanceConfig instance = Selected.Config;
			IProgress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			IProgress<DownloadProgress> dlProgress = new Progress<DownloadProgress>(delegate(DownloadProgress dp)
			{
				double p = dp.Stage switch
				{
					"Client JAR" => 0.05 + dp.Fraction * 0.15,
					"Libraries" => 0.20 + dp.Fraction * 0.35,
					"Assets" => 0.55 + dp.Fraction * 0.42,
					"Natives" => 0.98,
					"Ready" => 1.0,
					_ => dp.Fraction * 0.5
				};
				RunOnUi(delegate
				{
					StatusText = "Repairing · " + dp.Stage;
					StatusProgress = Math.Clamp(p, 0.04, 1.0);
					StatusDetail = dp.Detail;
				});
			});
			LauncherPaths paths = Paths;
			foreach (string item in await Task.Run(async () =>
			{
				var (text, text2) = ForgeInstaller.Split(instance.VersionId);
				if (text != null && text2 != null && !ForgeInstaller.IsInstalled(text, text2, paths))
				{
					progress.Report("installing Forge " + instance.VersionId);
					await ForgeInstaller.InstallAsync(text, text2, paths, progress, default, dlProgress).ConfigureAwait(continueOnCapturedContext: false);
				}
				return await Doctor.RepairAsync(instance, paths, progress, default, dlProgress).ConfigureAwait(continueOnCapturedContext: false);
			}))
			{
				AppendConsole("OK", item);
			}
			AppendConsole("OK", "repair finished - running the health check again");
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", ex.Message);
			Log("repair failed: " + ex);
		}
		finally
		{
			IsBusy = false;
			StatusIndeterminate = false;
		}
		await CheckAsync();
	}

	public async Task BackupAsync()
	{
		if (Selected == null || IsBusy || IsRunning)
		{
			return;
		}
		IsBusy = true;
		StatusText = "Backing up worlds";
		StatusDetail = "Only worlds that changed since the last backup are copied.";
		StatusIndeterminate = true;
		AppendConsole("INFO", "world backup started");
		try
		{
			InstanceConfig instance = Selected.Config;
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			List<BackupInfo> list = await Task.Run(() => WorldBackup.CreateChanged(Paths, instance, progress));
			AppendConsole("OK", (list.Count == 0) ? "nothing changed since the last backup" : (list.Count + " backup(s) written"));
			StatusText = "Backups ready";
			StatusDetail = ((list.Count == 0) ? "All worlds already up to date." : (list.Count + " world(s) backed up."));
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", ex.Message);
			Log("backup failed: " + ex);
		}
		finally
		{
			IsBusy = false;
			StatusIndeterminate = false;
		}
		if (Selected != null)
		{
			await LoadInstanceDetailsAsync(Selected);
		}
	}

	public async Task RestoreBackupAsync(SimpleRow row)
	{
		if (Selected == null || IsBusy || IsRunning || !(row?.Tag is BackupInfo backup))
		{
			return;
		}
		if (!(await ConfirmAsync("Restore backup “" + backup.WorldName + "”?", "Nova restores into a fresh copy inside saves\\ so your existing world is never overwritten or deleted.", "RESTORE WORLD")))
		{
			return;
		}
		IsBusy = true;
		StatusText = "Restoring world";
		StatusDetail = backup.FileName;
		StatusIndeterminate = true;
		try
		{
			InstanceConfig instance = Selected.Config;
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			string restored = await Task.Run(delegate
			{
				if (!WorldBackup.Verify(backup, out var msg))
				{
					throw new InvalidOperationException("Backup verification failed: " + msg);
				}
				return WorldBackup.Restore(Paths, instance, backup, null, replace: false, progress);
			});
			AppendConsole("OK", "world restored to: " + restored);
			StatusText = "World restored";
			StatusDetail = Path.GetFileName(restored);
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", ex.Message);
			StatusText = "Restore failed";
			StatusDetail = ex.Message;
			Log("restore failed: " + ex);
		}
		finally
		{
			IsBusy = false;
			StatusIndeterminate = false;
		}
		if (Selected != null)
		{
			await LoadInstanceDetailsAsync(Selected);
		}
	}

	public void OpenBackupsFolder()
	{
		if (Selected == null)
		{
			return;
		}
		string dir = WorldBackup.BackupRoot(Paths, Selected.Id);
		Directory.CreateDirectory(dir);
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	public void OpenWorldsFolder()
	{
		if (Selected == null)
		{
			return;
		}
		string dir = Path.Combine(Selected.GameDirectory, "saves");
		Directory.CreateDirectory(dir);
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	public async Task LaunchAsync()
	{
		if (Selected == null || IsRunning)
		{
			return;
		}
		IsRunning = true;
		ConsoleLines.Clear();
		AppendConsole("OK", "launch requested for " + Selected.Name);
		FlushConsole();
		try
		{
			InstanceConfig instance = Selected.Config;
			StatusText = "Checking";
			StatusDetail = "Verifying the installation before launch…";
			StatusIndeterminate = true;
			DoctorReport doctorReport = await Task.Run(() => Doctor.RunAsync(instance, Paths, deep: true).GetAwaiter().GetResult());
			Checks.Clear();
			foreach (CheckItem item2 in doctorReport.Items)
			{
				Checks.Add(new CheckRow(item2.Area, item2.Status.ToString().ToUpperInvariant(), item2.Message, item2.Suggestion));
			}
			if (!doctorReport.Healthy)
			{
				foreach (CheckItem item3 in doctorReport.Items.Where((CheckItem i) => i.Status == CheckStatus.Fail))
				{
					AppendConsole("ERROR", item3.Area + ": " + item3.Message);
				}
				StatusIndeterminate = false;
				StatusText = "Cannot start";
				StatusDetail = "Fix the problems above or press REPAIR.";
				IsRunning = false;
				return;
			}
			ResolvedVersion version = doctorReport.Version;
			JavaInstall java = doctorReport.Java;
			AppendConsole("OK", $"version {version.Id} · {version.Files.Count} libraries · Java {java.Major}");
			if (instance.BackupWorldsBeforeLaunch)
			{
				StatusText = "Backing up";
				Progress<string> progress = new Progress<string>((string line) =>
				{
					AppendConsole("INFO", line);
				});
				AppendConsole("OK", (await Task.Run(() => WorldBackup.CreateChanged(Paths, instance, progress))).Count + " world(s) backed up before launch");
			}
			StatusText = "Preparing";
			NovaAccount account = _accountStore.Active(Settings);
			if (account.IsMicrosoft && (!account.ExpiresUtc.HasValue || account.ExpiresUtc < DateTime.UtcNow.AddMinutes(2.0)))
			{
				try
				{
					if (MicrosoftAuth.LooksLikeClientId(Settings.MicrosoftClientId))
					{
						using MicrosoftAuth auth = new MicrosoftAuth();
						NovaAccount novaAccount = await auth.RefreshAsync(Settings.MicrosoftClientId, account);
						if (novaAccount != null)
						{
							_accountStore.Save(novaAccount);
							account = novaAccount;
						}
					}
				}
				catch (Exception ex)
				{
					AppendConsole("WARN", "microsoft: could not refresh the session - " + ex.Message);
				}
			}
			AppendConsole("INFO", "player: " + account.Name + "  (" + account.KindLabel + ")");
			PlayerIdentity identity = account.Identity;
			string nativesDir = Path.Combine(Paths.Natives, instance.Id);
			string librariesDir = (string.IsNullOrWhiteSpace(instance.LibrariesHome) ? Paths.Libraries : instance.LibrariesHome);
			List<string> nativeJars = version.NativeFiles.Select((ResolvedFile n) => Path.Combine(librariesDir, n.RelativePath.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists).ToList();
			(LaunchPlan, int) tuple = await Task.Run(() =>
			{
				int item = NativeExtractor.ExtractAll(nativeJars, nativesDir);
				return (Plan: LaunchBuilder.Build(instance, version, Paths, java, nativesDir, identity), Extracted: item);
			});
			AppendConsole("INFO", tuple.Item2 + " native file(s) prepared");
			foreach (string note in tuple.Item1.Notes)
			{
				AppendConsole("WARN", note);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo(tuple.Item1.JavaPath)
			{
				WorkingDirectory = tuple.Item1.WorkingDirectory,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (string argument in tuple.Item1.Arguments)
			{
				processStartInfo.ArgumentList.Add(argument);
			}
			string text = Path.Combine(instance.GameDirectory, "logs");
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, "launcher-console.log");
			string latestLog = Path.Combine(text, "latest.log");
			StreamWriter writer = new StreamWriter(path, append: false)
			{
				AutoFlush = true
			};
			Process process = new Process
			{
				StartInfo = processStartInfo,
				EnableRaisingEvents = true
			};
			process.OutputDataReceived += (object _, DataReceivedEventArgs e) =>
			{
				if (e.Data == null)
				{
					return;
				}
				try
				{
					writer.WriteLine(e.Data);
				}
				catch
				{
				}
			};
			process.ErrorDataReceived += (object _, DataReceivedEventArgs e) =>
			{
				if (e.Data == null)
				{
					return;
				}
				try
				{
					writer.WriteLine("[stderr] " + e.Data);
				}
				catch
				{
				}
			};
			_gameProcess = process;
			instance.LastPlayedUtc = DateTime.UtcNow;
			new InstanceStore(Paths).Save(instance);
			PlayedSummary = Selected?.LastPlayed ?? PlayedSummary;
			_launchStartedUtc = DateTime.UtcNow;
			CrashBannerVisible = false;
			CrashPath = "";
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			StatusIndeterminate = false;
			StatusProgress = 0.3;
			StatusText = "Starting";
			StatusDetail = "Minecraft is loading. A 140 mod pack usually takes 6-11 minutes.";
			AppendConsole("OK", "java started - " + Path.GetFileName(tuple.Item1.JavaPath));
			Log("launch started: " + tuple.Item1.JavaPath);
			StartLogTail(latestLog);
			WatchGameAsync(process, writer, latestLog);
		}
		catch (Exception ex2)
		{
			AppendConsole("ERROR", ex2.Message);
			StatusText = "Launch failed";
			StatusDetail = ex2.Message;
			StatusIndeterminate = false;
			IsRunning = false;
			Log("launch failed: " + ex2);
		}
	}

	private void StartLogTail(string latestLog)
	{
		try
		{
			_tailer?.Dispose();
		}
		catch
		{
		}
		_tailer = new LogTailer();
		_tailer.Follow(latestLog, (string line) =>
		{
			if (ShouldShow(line))
			{
				string level = LevelOf(line);
				string text = CleanLine(line);
				RunOnUi(() =>
				{
					AppendConsole(level, text);
					if (line.Contains("Setting user", StringComparison.OrdinalIgnoreCase))
					{
						StatusText = "Loading";
						StatusProgress = 0.45;
						StatusDetail = "Session started, loading mods…";
					}
					else if (line.Contains("Forge Mod Loader has successfully loaded", StringComparison.OrdinalIgnoreCase))
					{
						StatusText = "Mods loaded";
						StatusProgress = 0.8;
						StatusDetail = CleanLine(line);
					}
					else if (line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase))
					{
						StatusText = "Menu";
						StatusProgress = 1.0;
						StatusDetail = "Reached the main menu.";
					}
				});
			}
		});
	}

	private async Task WatchGameAsync(Process process, StreamWriter writer, string latestLog)
	{
		try
		{
			await process.WaitForExitAsync();
		}
		catch (Exception ex)
		{
			Log("WaitForExit failed: " + ex.Message);
		}
		finally
		{
			OnGameExited(process, writer, latestLog);
		}
	}

	private void ShowRememberedCrash()
	{
		if (IsRunning)
		{
			return;
		}
		string? text = Selected?.Config.LastExitSummary;
		if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("crash", StringComparison.OrdinalIgnoreCase))
		{
			CrashBannerVisible = false;
			CrashPath = "";
			CrashSuspectFile = "";
			return;
		}
		CrashTitle = "LAST SESSION CRASHED";
		CrashDetail = ((text.Length > 240) ? (text.Substring(0, 240) + "...") : text);
		CrashMeta = Selected?.Name ?? "";
		string parsedSuspect = "";
		int lb = text.LastIndexOf('[');
		int rb = text.LastIndexOf(']');
		if (lb >= 0 && rb > lb + 1)
		{
			string cand = text.Substring(lb + 1, rb - lb - 1).Trim();
			if (Selected != null && File.Exists(Path.Combine(Selected.GameDirectory, "mods", cand)))
			{
				parsedSuspect = cand;
			}
		}
		CrashSuspectFile = parsedSuspect;
		string crashPath = "";
		try
		{
			string text2 = ((Selected == null) ? null : Path.Combine(Selected.GameDirectory, "crash-reports"));
			if (text2 != null && Directory.Exists(text2))
			{
				crashPath = (from f in new DirectoryInfo(text2).GetFiles("*.txt")
					orderby f.LastWriteTimeUtc descending
					select f).FirstOrDefault()?.FullName ?? "";
			}
		}
		catch
		{
			crashPath = "";
		}
		CrashPath = crashPath;
		CrashBannerVisible = true;
		(OpenCrashReportCommand as RelayCommand)?.Refresh();
	}

	private void PersistExitSummary(InstanceConfig? instance, int exitCode, CrashDiagnosis? crash, List<string> suspects)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			int value = (int)Math.Max(0.0, (DateTime.UtcNow - _launchStartedUtc).TotalMinutes);
			string text;
			if (crash == null)
			{
				text = $"exit {exitCode} after {value} min";
			}
			else
			{
				text = $"crash after {value} min - " + (crash.Description ?? "unknown");
				if (crash.Causes.Count > 0)
				{
					string text2 = text;
					List<string> causes = crash.Causes;
					text = text2 + " - " + causes[causes.Count - 1];
				}
				if (suspects.Count > 0)
				{
					text = text + "  [" + suspects[0] + "]";
				}
				if (text.Length > 240)
				{
					text = text.Substring(0, 240);
				}
			}
			instance.LastExitSummary = text;
			new InstanceStore(Paths).Save(instance);
		}
		catch (Exception ex)
		{
			Log("could not save the exit summary: " + ex.Message);
		}
	}

	private void ShowCrashBanner(int exitCode, CrashDiagnosis? crash, List<string> suspects)
	{
		if (crash == null && exitCode == 0)
		{
			CrashBannerVisible = false;
			CrashPath = "";
			CrashSuspectFile = "";
			return;
		}
		CrashSuspectFile = ((suspects.Count > 0) ? suspects[0] : "");
		CrashTitle = ((crash == null) ? ("GAME EXITED  ·  " + exitCode) : ("GAME CRASHED  ·  exit " + exitCode));
		List<string> list = new List<string>();
		string text = crash?.Description;
		if (text != null && text.Length > 0)
		{
			list.Add(text);
		}
		if (crash != null && crash.Causes.Count > 0)
		{
			List<string> causes = crash.Causes;
			list.Add(causes[causes.Count - 1]);
		}
		string text2 = ((list.Count > 0) ? string.Join("   ·   ", list) : "the game stopped without writing a crash report - read the console for the last lines");
		CrashDetail = ((text2.Length > 240) ? (text2.Substring(0, 240) + "...") : text2);
		List<string> list2 = new List<string> { DateTime.Now.ToString("HH:mm") };
		if (suspects.Count > 0)
		{
			list2.Add("suspect: " + string.Join(", ", suspects));
		}
		else if (crash != null)
		{
			list2.Add("no mod jar matched the stack");
		}
		CrashMeta = string.Join("   ·   ", list2);
		CrashPath = crash?.SourceFile ?? "";
		CrashBannerVisible = true;
		(OpenCrashReportCommand as RelayCommand)?.Refresh();
	}

	private void DisableCrashSuspect()
	{
		InstanceVm? selected = Selected;
		string suspect = _crashSuspectFile;
		if (selected == null || string.IsNullOrWhiteSpace(suspect))
		{
			return;
		}
		string modsDir = Path.Combine(selected.GameDirectory, "mods");
		if (ModManager.SetEnabled(modsDir, suspect, enabled: false))
		{
			AppendConsole("OK", "disabled crash suspect: " + ModManager.DisplayName(suspect));
			FlushConsole();
			CrashSuspectFile = "";
			CrashDetail = "Disabled " + ModManager.DisplayName(suspect) + " — press LAUNCH to test without it.";
			_ = LoadInstanceDetailsAsync(selected);
		}
		else
		{
			AppendConsole("WARN", "could not disable " + suspect + " (already disabled or missing)");
			FlushConsole();
			CrashSuspectFile = "";
		}
	}

	private void OpenCrashReport()
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(_crashPath) && File.Exists(_crashPath))
			{
				Process.Start(new ProcessStartInfo(_crashPath)
				{
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			Log("open crash report failed: " + ex.Message);
		}
	}

	private void OpenCrashFolder()
	{
		try
		{
			string text = (string.IsNullOrWhiteSpace(_crashPath) ? null : Path.GetDirectoryName(_crashPath));
			if (text == null && Selected != null)
			{
				text = Path.Combine(Selected.GameDirectory, "crash-reports");
			}
			if (text == null || !Directory.Exists(text))
			{
				AppendConsole("WARN", "no crash-reports folder yet");
				return;
			}
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + text + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log("open crash folder failed: " + ex.Message);
		}
	}

	private void OnGameExited(Process process, StreamWriter writer, string latestLog)
	{
		try
		{
			int exitCode = -1;
			try
			{
				exitCode = process.ExitCode;
			}
			catch
			{
			}
			try
			{
				_tailer?.Dispose();
			}
			catch
			{
			}
			_tailer = null;
			try
			{
				writer.Flush();
				writer.Dispose();
			}
			catch
			{
			}
			Log($"game exited with code {exitCode}");
			InstanceConfig instance = Selected?.Config;
			List<CrashFinding> findings = new List<CrashFinding>();
			string reportPath = null;
			List<string> suspects = new List<string>();
			CrashDiagnosis crash = null;
			try
			{
				findings = (from f in CrashAnalyzer.AnalyzeFile(latestLog)
					where f.Severity != "INFO"
					select f).ToList();
				if (instance != null)
				{
					string text = CrashDiagnostics.NewestCrashFileSince(instance, _launchStartedUtc);
					if (text != null)
					{
						crash = CrashDiagnostics.AnalyzeFile(text, Path.Combine(instance.GameDirectory, "mods"));
						if (crash.Suspects.Count > 0)
						{
							suspects = (from s in crash.Suspects.Take(3)
								select s.ModFile).ToList();
						}
						try
						{
							reportPath = CrashDiagnostics.Write(Paths, instance, crash);
						}
						catch (Exception ex)
						{
							Log("could not write the crash summary: " + ex.Message);
						}
					}
				}
			}
			catch (Exception ex2)
			{
				Log("post-exit analysis failed: " + ex2);
			}
			RunOnUi(() =>
			{
				AppendConsole((exitCode == 0) ? "OK" : "WARN", "game exited with code " + exitCode);
				foreach (CrashFinding item in findings.Take(4))
				{
					AppendConsole("WARN", item.Title + " - " + item.Suggestion);
				}
				if (suspects.Count > 0)
				{
					AppendConsole("ERROR", "suspect mods: " + string.Join(", ", suspects));
				}
				if (crash != null)
				{
					MainViewModel mainViewModel = this;
					string? obj5 = crash.Description ?? "unknown";
					string text2;
					if (crash.Causes.Count <= 0)
					{
						text2 = "";
					}
					else
					{
						List<string> causes = crash.Causes;
						text2 = " - " + causes[causes.Count - 1];
					}
					mainViewModel.AppendConsole("ERROR", "crash: " + obj5 + text2);
					AppendConsole("INFO", "crash source: " + crash.SourceFile);
				}
				if (reportPath != null)
				{
					AppendConsole("INFO", "crash report written to " + reportPath);
				}
				PersistExitSummary(instance, exitCode, crash, suspects);
				ShowCrashBanner(exitCode, crash, suspects);
				StatusProgress = 0.0;
				StatusIndeterminate = false;
				StatusText = ((exitCode == 0) ? "Stopped" : ("Game exited (" + exitCode + ")"));
				StatusDetail = "Press PLAY to start again.";
				IsRunning = false;
				_gameProcess = null;
			});
			try
			{
				process.Dispose();
			}
			catch
			{
			}
		}
		catch (Exception ex3)
		{
			Log("OnGameExited failed: " + ex3);
			RunOnUi(() =>
			{
				StatusIndeterminate = false;
				StatusText = "Stopped";
				StatusDetail = "The game exited.";
				IsRunning = false;
			});
		}
	}

	public void ShutdownGame()
	{
		try
		{
			_tailer?.Dispose();
		}
		catch
		{
		}
		StopGame();
	}

	private void StopGame()
	{
		Process gameProcess = _gameProcess;
		if (gameProcess == null)
		{
			return;
		}
		try
		{
			if (!gameProcess.HasExited)
			{
				AppendConsole("WARN", "sending stop request…");
				gameProcess.Kill(entireProcessTree: true);
			}
		}
		catch (Exception ex)
		{
			AppendConsole("ERROR", ex.Message);
			Log("stop failed: " + ex);
		}
	}

	public async Task<string?> ImportPackAsync(string zipPath)
	{
		if (IsBusy || IsRunning)
		{
			return "the launcher is busy";
		}
		IsBusy = true;
		StatusIndeterminate = true;
		StatusText = "Importing pack";
		StatusDetail = "Reading " + Path.GetFileName(zipPath);
		AppendConsole("INFO", "import started: " + zipPath);
		FlushConsole();
		try
		{
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			LauncherPaths paths = Paths;
			string? cfKey = Settings.CurseForgeKey;
			PackImportResult result = await Task.Run(() => PackImporter.ImportAsync(zipPath, paths, null, null, progress, default, cfKey).GetAwaiter().GetResult());
			await RefreshAsync();
			InstanceVm instanceVm = Instances.FirstOrDefault((InstanceVm i) => i.Id == result.InstanceId);
			if (instanceVm != null)
			{
				Selected = instanceVm;
			}
			foreach (string note in result.Notes)
			{
				AppendConsole("INFO", note);
			}
			if (result.Missing.Count > 0)
			{
				AppendConsole("WARN", result.Missing.Count + " file(s) could not be downloaded");
			}
			AppendConsole((result.FilesFailed == 0) ? "OK" : "WARN", "import finished: " + result.Summary);
			StatusIndeterminate = false;
			StatusProgress = 1.0;
			StatusText = "Pack imported";
			StatusDetail = result.Name + " · " + result.Summary + " · run CHECK, then REPAIR for any missing files";
			Log("pack imported: " + result.InstanceId + " " + result.Summary);
			return null;
		}
		catch (Exception ex)
		{
			StatusIndeterminate = false;
			StatusText = "Import failed";
			StatusDetail = ex.Message;
			AppendConsole("ERROR", ex.Message);
			Log("import failed: " + ex);
			return ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	public async Task<string?> ExportPackAsync(string outputZip, bool includeWorlds = false)
	{
		if (Selected == null)
		{
			return "no instance selected";
		}
		if (IsBusy || IsRunning)
		{
			return "the launcher is busy";
		}
		IsBusy = true;
		StatusIndeterminate = true;
		StatusText = "Exporting pack";
		StatusDetail = "Writing " + Path.GetFileName(outputZip);
		AppendConsole("INFO", "export started: " + outputZip);
		try
		{
			InstanceConfig config = Selected.Config;
			Progress<string> progress = new Progress<string>((string line) =>
			{
				AppendConsole("INFO", line);
			});
			var (text, value, num) = await Task.Run(() => PackExporter.Export(config, outputZip, includeWorlds, progress));
			AppendConsole("OK", $"export finished: {value} file(s), {num / 1024 / 1024} MB at {text}");
			StatusIndeterminate = false;
			StatusProgress = 1.0;
			StatusText = "Pack exported";
			StatusDetail = Path.GetFileName(text) + " · " + value + " files";
			Log("pack exported: " + text);
			return null;
		}
		catch (Exception ex)
		{
			StatusIndeterminate = false;
			StatusText = "Export failed";
			StatusDetail = ex.Message;
			AppendConsole("ERROR", ex.Message);
			Log("export failed: " + ex);
			return ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	public static PackInfo InspectPack(string zipPath)
	{
		return PackInspector.Inspect(zipPath);
	}

	private void OpenGameFolder()
	{
		string text = Selected?.GameDirectory;
		if (string.IsNullOrEmpty(text) || !Directory.Exists(text))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + text + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log("open folder failed: " + ex.Message);
		}
	}

	private static bool ShouldShow(string line)
	{
		if (line.Contains("Missing sound for event", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (line.Contains("Potentially Dangerous alternative prefix", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (line.StartsWith("\tat ") || line.StartsWith("Caused by"))
		{
			return false;
		}
		if (line.Contains("/ERROR]") || line.Contains("/FATAL]") || line.Contains("/WARN]"))
		{
			return true;
		}
		if (!line.Contains("Mod Loader has", StringComparison.OrdinalIgnoreCase) && !line.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase) && !line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase) && !line.Contains("Setting user", StringComparison.OrdinalIgnoreCase) && !line.Contains("Stopping!", StringComparison.OrdinalIgnoreCase))
		{
			return line.Contains("Forge Mod Loader version", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string LevelOf(string line)
	{
		if (line.Contains("/FATAL]"))
		{
			return "FATAL";
		}
		if (line.Contains("/ERROR]"))
		{
			return "ERROR";
		}
		if (line.Contains("/WARN]"))
		{
			return "WARN";
		}
		return "INFO";
	}

	private static string CleanLine(string line)
	{
		string text = line.Trim();
		int num = text.IndexOf("]: ", StringComparison.Ordinal);
		if (num > 0 && num < 90)
		{
			string text2 = text;
			int num2 = num + 3;
			text = text2.Substring(num2, text2.Length - num2);
		}
		if (text.Length <= 180)
		{
			return text;
		}
		return text.Substring(0, 180) + "…";
	}

	public async Task CheckLauncherUpdateAsync(bool manual)
	{
		if (IsLauncherUpdateBusy)
		{
			return;
		}
		IsLauncherUpdateBusy = true;
		if (manual)
		{
			LauncherUpdateStatus = "Checking GitHub Releases (" + UpdateRepoSlug + ")…";
		}
		try
		{
			string repo = UpdateRepoSlug;
			LauncherUpdateInfo? info = await Task.Run(() => LauncherUpdater.CheckForUpdateAsync(repo, includeSameOrOlder: false).GetAwaiter().GetResult());
			_pendingLauncherUpdate = info;
			Raise("HasLauncherUpdate");
			Raise("LauncherUpdateTitle");
			Raise("LauncherUpdateDetail");
			Raise("CanApplyLauncherUpdate");
			Raise("LauncherUpdateActionLabel");
			(ApplyLauncherUpdateCommand as RelayCommand)?.Refresh();

			if (info != null)
			{
				LauncherUpdateStatus = $"Update v{info.LatestVersion} ready ({info.Title}) — worlds & mods stay intact.";
				LauncherUpdateBannerVisible = true;
				AppendConsole("OK", $"updater: new release v{info.LatestVersion} available on {repo}");
			}
			else if (manual)
			{
				LauncherUpdateStatus = $"Up to date · running latest v{LauncherUpdater.CurrentVersion} ({repo})";
				AppendConsole("OK", $"updater: v{LauncherUpdater.CurrentVersion} is already the latest release");
			}
		}
		catch (Exception ex)
		{
			if (manual)
			{
				LauncherUpdateStatus = "Update check failed: " + ex.Message;
				AppendConsole("WARN", "updater check failed: " + ex.Message);
			}
			Log("launcher update check failed: " + ex);
		}
		finally
		{
			IsLauncherUpdateBusy = false;
			FlushConsole();
		}
	}

	public async Task ApplyLauncherUpdateAsync()
	{
		if (_pendingLauncherUpdate == null || IsLauncherUpdateBusy)
		{
			return;
		}
		if (IsRunning)
		{
			LauncherUpdateStatus = "Close the running Minecraft session before updating Nova.";
			return;
		}
		IsLauncherUpdateBusy = true;
		LauncherUpdateInfo update = _pendingLauncherUpdate;
		LauncherUpdateStatus = $"Downloading Nova v{update.LatestVersion}…";
		AppendConsole("INFO", $"updater: downloading Nova v{update.LatestVersion} ({update.AssetName})");
		FlushConsole();
		try
		{
			IProgress<string> prog = new Progress<string>((string line) =>
			{
				RunOnUi(() => LauncherUpdateStatus = line);
				AppendConsole("INFO", "updater: " + line);
			});
			string stagedDir = await Task.Run(() => LauncherUpdater.StageUpdateAsync(Paths, update, prog).GetAwaiter().GetResult());
			string targetBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
			string uiLog = Path.Combine(Paths.Root, "logs", "nova-ui.log");
			int currentPid = Environment.ProcessId;

			Log($"updater staged v{update.LatestVersion}; spawning swapper into {targetBin} (pid={currentPid})");
			LauncherUpdater.LaunchDetachedSwapper(stagedDir, targetBin, currentPid, relaunchGui: true, uiLog);
			Application.Current?.Shutdown();
		}
		catch (Exception ex)
		{
			LauncherUpdateStatus = "Update failed: " + ex.Message;
			AppendConsole("ERROR", "updater failed: " + ex.Message);
			Log("launcher update apply failed: " + ex);
			IsLauncherUpdateBusy = false;
			FlushConsole();
		}
	}

	private void OpenLauncherReleaseUrl()
	{
		string url = _pendingLauncherUpdate?.HtmlUrl ?? ("https://github.com/" + UpdateRepoSlug + "/releases");
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void AppendConsole(string level, string text)
	{
		_pendingLines.Enqueue(new ConsoleLine(DateTime.Now.ToString("HH:mm:ss"), level, text));
	}

	private void FlushConsole()
	{
		bool flag = false;
		ConsoleLine result;
		while (_pendingLines.TryDequeue(out result))
		{
			ConsoleLines.Add(result);
			flag = true;
		}
		if (flag)
		{
			while (ConsoleLines.Count > 250)
			{
				ConsoleLines.RemoveAt(0);
			}
		}
	}
}
