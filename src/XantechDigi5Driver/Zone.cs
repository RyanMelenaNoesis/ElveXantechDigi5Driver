using CodecoreTechnologies.Elve.DriverFramework;
using CodecoreTechnologies.Elve.DriverFramework.Communication;
using System;

namespace OnClickInc.Elve
{
	public class Zone
	{
		#region Constants

		protected const int BALANCE_OFFSET = 6;
		protected const int BASS_OFFSET = 6;
		protected const int BUG_FREE_FIRMWARE_VERSION = 109;
		protected const int LOCAL_SOURCE = 9;
		protected const int LOCAL_SOURCE_NUMBER = 5;
		protected const int MAX_BALANCE = 11;
		protected const int MAX_BASS_LEVEL = 11;
		protected const int MAX_SOURCE = 4;
		protected const int MAX_TREBLE_LEVEL = 11;
		protected const int MAX_VOLUME = 21;
		protected const int MIN_BALANCE = 1;
		protected const int MIN_BASS_LEVEL = 1;
		protected const int MIN_SOURCE = 1;
		protected const int MIN_TREBLE_LEVEL = 1;
		protected const int MIN_VOLUME = 0;
		protected const int TREBLE_OFFSET = 6;

		#endregion Constants

		#region Events

		public event EventHandler BalanceChanged;

		public event EventHandler BassLevelChanged;

		public event EventHandler MuteStateChanged;

		public event EventHandler PowerStateChanged;

		public event EventHandler SourceChanged;

		public event EventHandler TrebleLevelChanged;

		public event EventHandler VolumeChanged;

		#endregion Events

		#region Fields

		protected int _balance;
		protected int _bassLevel;
		protected ICommunication _comm;
		protected bool _doNotDisturbEnabled;
		protected bool _dynamicRangeCorrectionEnabled;
		protected bool _isMuted;
		protected bool _isPowerOn;
		protected ILogger _logger;
		protected bool _loudnessEnabled;
		protected string _name;
		protected int _source;
		protected int _trebleLevel;
		protected int _volume;
		protected bool _wholeHouseModeEnabled;

		#endregion Fields

		#region Properties

		public int Balance
		{
			get { return this._balance + 5; }
			protected set
			{
				if (value != this._balance)
				{
					this._balance = value;
					this.OnBalanceChanged();
				}
			}
		}

		public int BassLevel
		{
			get { return this._bassLevel; }
			protected set
			{
				if (value != this._bassLevel)
				{
					this._bassLevel = value;
					this.OnBassLevelChanged();
				}
			}
		}

		public int HubFirmwareVersion { get; set; }

		public bool IsMuted
		{
			get { return this._isMuted; }
			protected set
			{
				if (value != this._isMuted)
				{
					this._isMuted = value;
					this.OnMuteStateChanged();
				}
			}
		}

		public bool IsPowerOn
		{
			get { return this._isPowerOn; }
			protected set
			{
				if (value != this._isPowerOn)
				{
					this._isPowerOn = value;
					this.OnPowerStateChanged();
				}
			}
		}

		public int Number { get; protected set; }

		public int Source
		{
			get { return this._source; }
			protected set
			{
				if (value != this._source)
				{
					this._source = value;
					this.OnSourceChanged();
				}
			}
		}

		public int TrebleLevel
		{
			get { return this._trebleLevel; }
			protected set
			{
				if (value != this._trebleLevel)
				{
					this._trebleLevel = value;
					this.OnTrebleLevelChanged();
				}
			}
		}

		public int Volume
		{
			get { return this._volume; }
			protected set
			{
				if (value != this._volume)
				{
					this._volume = value;
					this.OnVolumeChanged();
				}
			}
		}

		private string ZoneCommandPrefix { get { return "!" + this.Number.ToString(); } }

		private string ZoneQueryPrefix { get { return "?" + this.Number.ToString(); } }

		#endregion Properties

		#region Constructors

		public Zone(ICommunication comm, int zoneNumber, ILogger logger)
		{
			this._comm = comm;
			this.Number = zoneNumber;
			this._logger = logger;

			this._comm.ReceivedDelimitedString += new EventHandler<ReceivedDelimitedStringEventArgs>(ReceivedDelimitedString);
		}

		#endregion Constructors

		#region Methods

		public void DecrementBass()
		{
			this._comm.Send(this.ZoneCommandPrefix + "BD+");
			this.UpdateBassLevel();
		}

		public void DecrementSource()
		{
			this._comm.Send(this.ZoneCommandPrefix + "SD+");
			this.UpdateSource();
		}

		public void DecrementTreble()
		{
			this._comm.Send(this.ZoneCommandPrefix + "TD+");
			this.UpdateTrebleLevel();
		}

		public void DecrementVolume()
		{
			this._comm.Send(this.ZoneCommandPrefix + "VD+");
			this.UpdateVolume();
		}

		public void IncrementBass()
		{
			this._comm.Send(this.ZoneCommandPrefix + "BI+");
			this.UpdateBassLevel();
		}

		public void IncrementSource()
		{
			this._comm.Send(this.ZoneCommandPrefix + "SI+");
			this.UpdateSource();
		}

		public void IncrementTreble(bool updateAfterCommand)
		{
			this._comm.Send(this.ZoneCommandPrefix + "TI+");
			this.UpdateTrebleLevel();
		}

		public void IncrementVolume()
		{
			this._comm.Send(this.ZoneCommandPrefix + "VI+");
			this.UpdateVolume();
		}

		public void SetBalance(int balance)
		{
			int adjustedBalance = balance + BALANCE_OFFSET;

			if (adjustedBalance >= MIN_BALANCE && adjustedBalance <= MAX_BALANCE)
			{
				this._comm.Send(this.ZoneCommandPrefix + "BA" + (balance + 5).ToString() + "+");
				this.UpdateBalance();
			}
		}

		public void SetBassLevel(int bassLevel)
		{
			int adjustedBassLevel = bassLevel + BASS_OFFSET;

			if (adjustedBassLevel >= MIN_BASS_LEVEL && adjustedBassLevel <= MAX_BASS_LEVEL)
			{
				this._comm.Send(this.ZoneCommandPrefix + "BS" + bassLevel.ToString() + "+");
				this.UpdateBassLevel();
			}
		}

		public void SetDoNotDisturb(bool isDoNotDisturbEnabled)
		{
			this._comm.Send(this.ZoneCommandPrefix + "DD" + ((isDoNotDisturbEnabled) ? "1" : "0"));
		}

		public void SetDynamicRangeControl(bool isDynamicRangeControlEnabled)
		{
			this._comm.Send(this.ZoneCommandPrefix + "DR" + ((isDynamicRangeControlEnabled) ? "1" : "0"));
		}

		public void SetLoudnessState(bool isLoudnessEnabled)
		{
			this._comm.Send(this.ZoneCommandPrefix + "LO" + ((isLoudnessEnabled) ? "1" : "0") + "+");
		}

		public void SetMuteState(bool isMuted)
		{
			this._comm.Send(this.ZoneCommandPrefix + "MU" + ((isMuted) ? "1" : "0") + "+");
			this.UpdateMuteState();
		}

		public void SetPowerState(bool isPowerOn)
		{
			this._comm.Send(this.ZoneCommandPrefix + "PR" + ((isPowerOn) ? "1" : "0") + "+");
			this.UpdatePowerState();
		}

		public void SetSource(int source)
		{
			int adjustedSource = (source == LOCAL_SOURCE_NUMBER) ? LOCAL_SOURCE : source;

			if ((adjustedSource >= MIN_SOURCE && adjustedSource <= MAX_SOURCE) || adjustedSource == LOCAL_SOURCE)
			{
				this._comm.Send(this.ZoneCommandPrefix + "SS" + adjustedSource.ToString() + "+");
				this.UpdateSource();
			}
		}

		public void SetTrebleLevel(int trebleLevel)
		{
			int adjustedTrebleLevel = trebleLevel + TREBLE_OFFSET;

			if (adjustedTrebleLevel >= MIN_TREBLE_LEVEL && adjustedTrebleLevel <= MAX_TREBLE_LEVEL)
			{
				this._comm.Send(this.ZoneCommandPrefix + "TR" + trebleLevel.ToString() + "+");
				this.UpdateTrebleLevel();
			}
		}

		public void SetVolume(int volume)
		{
			if (volume >= MIN_VOLUME && volume <= MAX_VOLUME)
			{
				this._comm.Send(this.ZoneCommandPrefix + "VO" + volume.ToString() + "+");
				this.UpdateVolume();
			}
		}

		public void SetWholeHouseModeState(bool isWholeHouseModeEnabled)
		{
			this._comm.Send(this.ZoneCommandPrefix + "WH" + ((isWholeHouseModeEnabled) ? "1" : "0") + "+");
		}

		public void StepBalanceLeft()
		{
			this._comm.Send(this.ZoneCommandPrefix + "BL+");
			this.UpdateBalance();
		}

		public void StepBalanceRight()
		{
			this._comm.Send(this.ZoneCommandPrefix + "BR+");
			this.UpdateBalance();
		}

		public void ToggleMuteState()
		{
			this._comm.Send(this.ZoneCommandPrefix + "MT+");
			this.UpdateMuteState();
		}

		public void TogglePowerState()
		{
			this._comm.Send(this.ZoneCommandPrefix + "PT+");
			this.UpdatePowerState();
		}

		public void UpdateStatus()
		{
			this._logger.Debug("Updating Zone [" + this.Number + "] Status.");

			this.UpdateBalance();
			this.UpdateBassLevel();
			this.UpdateMuteState();
			this.UpdatePowerState();
			this.UpdateTrebleLevel();
			this.UpdateSource();
			this.UpdateVolume();
		}

		private void ReceivedDelimitedString(object sender, ReceivedDelimitedStringEventArgs e)
		{
			if (e.RawResponse.StartsWith(this.ZoneQueryPrefix))
			{
				this._logger.Debug("Zone [" + this.Number.ToString() + "] handling response [" + e.RawResponse + "].");

				string command = e.RawResponse.Substring(2, 2);
				int commandValue = (e.MessageIncludesDelimiter) ? int.Parse(e.RawResponse.Substring(4, e.RawResponse.Length - 5)) : int.Parse(e.RawResponse.Substring(4, e.RawResponse.Length - 4));

				this._logger.Debug("Zone [" + this.Number.ToString() + "] processing response of type [" + command + "] with value [" + commandValue.ToString() + "]");

				switch (command)
				{
					// Zone Power
					case "PR":
						this.IsPowerOn = (commandValue == 1);
						break;
					// Input (Source) Select
					case "SS":
						this.Source = (commandValue == LOCAL_SOURCE) ? LOCAL_SOURCE_NUMBER : commandValue;
						break;
					// Volume
					case "VO":
						this.Volume = commandValue;
						break;
					// Mute
					case "MU":
						this.IsMuted = (commandValue == 1);
						break;
					// Treble
					case "TR":
						this.TrebleLevel = commandValue + TREBLE_OFFSET;
						break;
					// Bass
					case "BS":
						this.BassLevel = commandValue + BASS_OFFSET;
						break;
					// Balance
					case "BA":
						this.Balance = commandValue + BALANCE_OFFSET;
						break;

					default:
						break;
				}
			}
		}

		private void UpdateBalance()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "BA+"); }
		}

		private void UpdateBassLevel()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "BS+"); }
		}

		private void UpdateMuteState()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "MU+"); }
		}

		private void UpdatePowerState()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "PR+"); }
		}

		private void UpdateSource()
		{
			this._comm.Send(this.ZoneQueryPrefix + "SS+");
		}

		private void UpdateTrebleLevel()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "TR+"); }
		}

		private void UpdateVolume()
		{
			if (this.HubFirmwareVersion >= BUG_FREE_FIRMWARE_VERSION) { this._comm.Send(this.ZoneQueryPrefix + "VO+"); }
		}

		#endregion Methods

		#region Event Handlers

		private void OnBalanceChanged()
		{
			if (this.BalanceChanged != null)
			{
				this.BalanceChanged(this, new EventArgs());
			}
		}

		private void OnBassLevelChanged()
		{
			if (this.BassLevelChanged != null)
			{
				this.BassLevelChanged(this, new EventArgs());
			}
		}

		private void OnMuteStateChanged()
		{
			if (this.MuteStateChanged != null)
			{
				this.MuteStateChanged(this, new EventArgs());
			}
		}

		private void OnPowerStateChanged()
		{
			if (this.PowerStateChanged != null)
			{
				this.PowerStateChanged(this, new EventArgs());
			}
		}

		private void OnSourceChanged()
		{
			if (this.SourceChanged != null)
			{
				this.SourceChanged(this, new EventArgs());
			}
		}

		private void OnTrebleLevelChanged()
		{
			if (this.TrebleLevelChanged != null)
			{
				this.TrebleLevelChanged(this, new EventArgs());
			}
		}

		private void OnVolumeChanged()
		{
			if (this.VolumeChanged != null)
			{
				this.VolumeChanged(this, new EventArgs());
			}
		}

		#endregion Event Handlers
	}
}