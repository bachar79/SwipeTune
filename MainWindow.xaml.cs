using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using NAudio.CoreAudioApi;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RawInput.Touchpad
{
	public partial class MainWindow : Window
	{
		// Dependency Properties
		public static readonly DependencyProperty StatusMessageProperty =
	DependencyProperty.Register("StatusMessage", typeof(string), typeof(MainWindow),
	new PropertyMetadata("Ready"));

		public string StatusMessage
		{
			get => (string)GetValue(StatusMessageProperty);
			set => SetValue(StatusMessageProperty, value);
		}
		public static readonly DependencyProperty SwipeStatusMessageProperty =
	DependencyProperty.Register("SwipeStatusMessage", typeof(string), typeof(MainWindow),
	new PropertyMetadata("Ready"));

		public string SwipeStatusMessage
		{
			get => (string)GetValue(SwipeStatusMessageProperty);
			set => SetValue(SwipeStatusMessageProperty, value);
		}
		public static readonly DependencyProperty TouchpadExistsProperty =
			DependencyProperty.Register("TouchpadExists", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

		public static readonly DependencyProperty TouchpadContactsProperty =
			DependencyProperty.Register("TouchpadContacts", typeof(string), typeof(MainWindow), new PropertyMetadata(null));

		public static readonly DependencyProperty VolumeProperty =
			DependencyProperty.Register("Volume", typeof(float), typeof(MainWindow), new PropertyMetadata(0f));

		public static readonly DependencyProperty BrightnessProperty =
			DependencyProperty.Register("Brightness", typeof(int), typeof(MainWindow), new PropertyMetadata(0));

		// Property accessors
		public bool TouchpadExists
		{
			get => (bool)GetValue(TouchpadExistsProperty);
			set => SetValue(TouchpadExistsProperty, value);
		}

		public string TouchpadContacts
		{
			get => (string)GetValue(TouchpadContactsProperty);
			set => SetValue(TouchpadContactsProperty, value);
		}

		public float Volume
		{
			get => (float)GetValue(VolumeProperty);
			set => SetValue(VolumeProperty, value);
		}

		public int Brightness
		{
			get => (int)GetValue(BrightnessProperty);
			set => SetValue(BrightnessProperty, value);
		}

		// Private fields
		private MMDevice _audioDevice;
		private HwndSource _targetSource;


		// Touchpad constants
		private const int TOUCHPAD_WIDTH = 2650;
		private const double EDGE_THRESHOLD = 0.1;  // 10% threshold for edge detection
		[DllImport("user32.dll")]
		private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

		private const int WM_APPCOMMAND = 0x0319;
		private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
		private const int APPCOMMAND_VOLUME_DOWN = 0x90000;
		private const int APPCOMMAND_VOLUME_UP = 0xA0000;
		public MainWindow()
		{
			InitializeAudioDevice();
			InitializeBrightness();
			DataContext = this;
			this.Loaded += MainWindow_Loaded;
		}

		private void InitializeBrightness()
		{
			Brightness = BrightnessController.Get();
		}
		private void InitializeAudioDevice()
		{
			var deviceEnumerator = new MMDeviceEnumerator();
			_audioDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

			// Get the raw volume value
			float rawVolume = _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100;

			// Convert to integer by rounding to remove decimal places
			Volume = (int)Math.Round(rawVolume);
		}
		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			var windowInteropHelper = new WindowInteropHelper(this);
			var handle = windowInteropHelper.Handle;

			TouchpadExists = TouchpadHelper.Exists();


			if (TouchpadExists)
			{
				TouchpadHelper.RegisterInput(handle);
			}

			_targetSource = HwndSource.FromHwnd(handle);
			_targetSource?.AddHook(WndProc);
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			e.Cancel = true;
			this.Hide();
			base.OnClosing(e);
		}
		private TouchpadContact? _lastContact;
		private DateTime _contactStartTime;
		private const int LONG_PRESS_THRESHOLD_MS = 1000; // 800ms for long press
		private const int POSITION_TOLERANCE = 50; // Tolerance for position changes
		public struct SwipeInfo
		{
			public bool IsSwipe;
			public TouchpadContact Start;
			public TouchpadContact End;
			public string Direction;

			public override string ToString()
			{
				if (!IsSwipe) return "No swipe detected";
				return $"Swipe {Direction}: From ({Start.X},{Start.Y}) to ({End.X},{End.Y})";
			}
		}

		// Add these fields to your class
		private TouchpadContact? _swipeStartContact;
		private const int SWIPE_THRESHOLD = 200;
		private List<string> swipeSequence = new List<string>();
		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == TouchpadHelper.WM_INPUT)
			{
				var contacts = TouchpadHelper.ParseInput(lParam);
				TouchpadContacts = string.Join(Environment.NewLine, contacts.Select(x => x.ToString()));

				// Reset tracking if no contacts
				if (contacts.Count() == 0)
				{
					if (_lastContact.HasValue)
					{
						// Check final position when finger is lifted
						SwipeInfo swipe = DetectSwipe(_lastContact.Value, true);
						if (swipe.IsSwipe)
						{
							StatusMessage = swipe.ToString();
							// Handle the completed swipe
						}

					}

					_lastContact = null;
					_swipeStartContact = null;
					return IntPtr.Zero;
				}



				foreach (var contact in contacts)
				{
					// First contact - initialize tracking
					if (!_lastContact.HasValue)
					{
						_lastContact = contact;
						_contactStartTime = DateTime.Now;
						_swipeStartContact = contact;
						continue;
					}

					// Check for swipe with each new contact


					// Check if position is roughly the same (within tolerance)
					bool samePosition = Math.Abs(contact.X - _lastContact.Value.X) <= POSITION_TOLERANCE &&
									   Math.Abs(contact.Y - _lastContact.Value.Y) <= POSITION_TOLERANCE;

					// If position changed significantly, reset timer
					if (!samePosition)
					{
						_contactStartTime = DateTime.Now;
					}

					// Calculate duration of contact
					TimeSpan contactDuration = DateTime.Now - _contactStartTime;
					bool isLongPress = samePosition && contactDuration.TotalMilliseconds >= LONG_PRESS_THRESHOLD_MS;
					SwipeInfo swipe = DetectSwipe(contact);

					// Process based on position and long press status
					if (IsLeftSideContact(contact))
					{
						StatusMessage = "Left side tap";
						// HandleVolumeControl(contact);
					}
					if (IsRightSideContact(contact))
					{
						StatusMessage = "Right side tap";
						// HandleBrightnessControl(contact);
					}
					if (IsMiddleSideContact(contact))
					{
						StatusMessage = "Middle tap";
						// Handle middle tap
					}


					if (swipe.IsSwipe)
					{
						//SwipeStatusMessage = swipe.ToString();
						ProcessSwipe(swipe);
						// Further actions based on swipe can go here
					}
					if (isLongPress)
					{
						StatusMessage = "Long press";
						// Handle middle long press
					}
					if (isLongPress && IsRightSideContact(contact) && !(swipe.Start.X < 2500))
					{

						HandleBrightnessControl(contact);
					}
					if (isLongPress && IsLeftSideContact(contact) && swipe.Start.X < 150)
					{

						HandleVolumeControl(contact);
					}

					//SwipeStatusMessage = swipe.IsSwipe.ToString();
					// Update last contact for next comparison
					_lastContact = contact;
				}
			}
			return IntPtr.Zero;
		}


		private bool isAlternateMode = false;

		private void Switch_Click(object sender, RoutedEventArgs e)
		{
			isAlternateMode = !isAlternateMode;

			if (isAlternateMode)
			{
				// Switch to alternate mode
				StatusMessage = "knob Mode Activated";
				// Add any other changes for alternate mode
			}
			else
			{
				// Switch back to normal mode
				StatusMessage = "Normal Mode Activated";
				// Add any other changes for normal mode
			}
		}

		// Function to detect swipes with each new contact
		private SwipeInfo DetectSwipe(TouchpadContact currentContact, bool isLiftOff = false)
		{
			SwipeInfo info = new SwipeInfo();

			// Initialize tracking if this is the first contact
			if (!_swipeStartContact.HasValue)
			{
				_swipeStartContact = currentContact;
				return info; // Not enough information for a swipe yet
			}

			// Calculate total movement
			int totalDeltaX = currentContact.X - _swipeStartContact.Value.X;
			int totalDeltaY = (currentContact.Y - _swipeStartContact.Value.Y) * 2;

			// Check if movement exceeds threshold
			bool isSwipe = Math.Abs(totalDeltaX) > SWIPE_THRESHOLD ||
						   Math.Abs(totalDeltaY) > SWIPE_THRESHOLD;

			if (!isSwipe && !isLiftOff)
			{
				return info; // Return with IsSwipe = false by default
			}

			// Determine swipe direction if it's a swipe or final lift-off
			if (isSwipe || isLiftOff)
			{
				string direction;
				if (Math.Abs(totalDeltaX) > Math.Abs(totalDeltaY))
				{
					// Horizontal swipe
					direction = totalDeltaX > 0 ? "Right" : "Left";
				}
				else
				{
					// Vertical swipe
					direction = totalDeltaY > 0 ? "Down" : "Up";
				}

				// Fill the swipe info
				info.IsSwipe = isSwipe; // Will be false for small movements on lift-off
				info.Start = _swipeStartContact.Value;
				info.End = currentContact;
				info.Direction = direction;

				// Reset tracking if it's a lift-off or a definite swipe
				if (isLiftOff || isSwipe)
				{
					_swipeStartContact = null;
				}
			}

			return info;
		}
		private void ProcessSwipe(SwipeInfo swipe)
		{
			string direction = swipe.Direction;
			if (direction != null)
			{
				// Only add the direction if it's different from the last one or if the sequence is empty
				if (swipeSequence.Count == 0 || swipeSequence[swipeSequence.Count - 1] != direction)
				{
					swipeSequence.Add(direction);
					if (swipeSequence.Count > 8)
						swipeSequence.RemoveAt(0);

					SwipeStatusMessage = string.Join(", ", swipeSequence);

					if (swipeSequence.SequenceEqual(new List<string> { "Up", "Right", "Down", "Left", "Up", "Right", "Down", "Left" }))
					{
						StatusMessage = "Swipe Pattern Detected!";
						IncreaseVolume();
						ShowVolumeOSD();
						swipeSequence.Clear();
					}
					if (swipeSequence.SequenceEqual(new List<string> { "Up", "Left", "Down", "Right", "Up", "Left", "Down", "Right" }))
					{
						StatusMessage = "Swipe Pattern Detected!";
						DecreaseVolume();
						ShowVolumeOSD();
						swipeSequence.Clear();
					}
				}
			}
		}
		private void ShowVolumeOSD()
		{
			// Get handle to the foreground window
			IntPtr hWnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

			// Send a fake volume command to show the OSD without actually changing volume
			// You can use either command: APPCOMMAND_VOLUME_UP or APPCOMMAND_VOLUME_DOWN
			// We'll use VOLUME_UP since we're just trying to show the OSD
			SendMessageW(hWnd, WM_APPCOMMAND, hWnd, (IntPtr)APPCOMMAND_VOLUME_UP);
			// Then immediately send the opposite command to negate the volume change
			SendMessageW(hWnd, WM_APPCOMMAND, hWnd, (IntPtr)APPCOMMAND_VOLUME_DOWN);
		}

		private bool IsLeftSideContact(TouchpadContact contact)
		{
			return contact.X < TOUCHPAD_WIDTH * EDGE_THRESHOLD;
		}

		private bool IsRightSideContact(TouchpadContact contact)
		{
			int rightSideStart = (int)(TOUCHPAD_WIDTH * (1 - EDGE_THRESHOLD));
			return contact.X > rightSideStart;
		}
		private bool IsMiddleSideContact(TouchpadContact contact)
		{
			return !(IsLeftSideContact(contact) || IsRightSideContact(contact));
		}
		private void HandleBrightnessControl(TouchpadContact contact)
		{
			int brightness = Math.Abs(contact.Y - 1300) / 13;
			brightness = Math.Clamp(brightness, 0, 100);

			BrightnessController.Set(brightness);
			Brightness = brightness;
		}

		private void HandleVolumeControl(TouchpadContact contact)
		{
			int volumePercentage = Math.Abs(contact.Y - 1300) / 13;
			volumePercentage = Math.Clamp(volumePercentage, 0, 100);

			float newVolume = volumePercentage / 100f;
			_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;
			Volume = volumePercentage;
			ShowVolumeOSD();
		}
		private void IncreaseVolume()
		{
			float step = 0.15f; // 15% step

			// Get current volume (0.0 to 1.0)
			float currentVolume = _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar;

			// Increase by 10%
			float newVolume = currentVolume + step;

			// Clamp value between 0.0 and 1.0
			newVolume = Math.Clamp(newVolume, 0.0f, 1.0f);

			// Apply the new volume
			_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;

			// Convert to percentage
			Volume = (int)(newVolume * 100);
		}
		private void DecreaseVolume()
		{
			float step = 0.15f; // 15% step

			// Get current volume (0.0 to 1.0)
			float currentVolume = _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar;

			// Increase by 10%
			float newVolume = currentVolume - step;

			// Clamp value between 0.0 and 1.0
			newVolume = Math.Clamp(newVolume, 0.0f, 1.0f);

			// Apply the new volume
			_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;

			// Convert to percentage
			Volume = (int)(newVolume * 100);
		}

	}

	public static class BrightnessController
	{
		public static int Get()
		{
			using var mclass = new ManagementClass("WmiMonitorBrightness")
			{
				Scope = new ManagementScope(@"\\.\root\wmi")
			};

			using var instances = mclass.GetInstances();
			foreach (ManagementObject instance in instances)
			{
				return (byte)instance.GetPropertyValue("CurrentBrightness");
			}
			return 0;
		}

		public static void Set(int brightness)
		{
			using var mclass = new ManagementClass("WmiMonitorBrightnessMethods")
			{
				Scope = new ManagementScope(@"\\.\root\wmi")
			};

			using var instances = mclass.GetInstances();
			var args = new object[] { 1, brightness };

			foreach (ManagementObject instance in instances)
			{
				instance.InvokeMethod("WmiSetBrightness", args);
			}
		}
	}
}