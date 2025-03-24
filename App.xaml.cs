using System.Windows;
using System.Windows.Forms; // Add this for NotifyIcon

namespace RawInput.Touchpad
{
	public partial class App : System.Windows.Application // Explicitly specify System.Windows.Application
	{
		private NotifyIcon _notifyIcon;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			// Create the notify icon in the system tray
			_notifyIcon = new NotifyIcon
			{
				Icon = System.Drawing.SystemIcons.Application,
				Visible = true,
				Text = "SwipeTune"
			};

			// Create context menu for the notify icon
			var contextMenu = new ContextMenuStrip();
			contextMenu.Items.Add("Show Window", null, (s, args) =>
			{
				MainWindow?.Show();
				MainWindow?.Activate();
			});
			contextMenu.Items.Add("Exit", null, (s, args) =>
			{
				Shutdown();
			});

			_notifyIcon.ContextMenuStrip = contextMenu;

			// Handle double-click on the notify icon
			_notifyIcon.MouseDoubleClick += (s, args) =>
			{
				MainWindow?.Show();
				MainWindow?.Activate();
			};

			// Handle application exit
			Exit += (s, args) =>
			{
				_notifyIcon.Visible = false;
				_notifyIcon.Dispose();
			};
		}
	}
}