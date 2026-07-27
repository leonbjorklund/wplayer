using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace WPlayer.Tests;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public void RegistersInstalledCurrentExecutable()
    {
        RunInTempDirectory(directory =>
        {
            var current = Path.Combine(directory, "current");
            Directory.CreateDirectory(current);
            var executable = Path.Combine(current, "WPlayer.exe");
            File.WriteAllText(executable, "");
            File.WriteAllText(Path.Combine(current, "sq.version"), "1.0.0");

            RunWithTemporaryKey(key =>
            {
                Assert.IsTrue(StartupRegistration.SetEnabled(key, true, executable));
                Assert.AreEqual($"\"{executable}\"", key.GetValue("WPlayer"));
            });
        });
    }

    [TestMethod]
    public void RejectsNonInstalledExecutableWithoutChangingRegistration()
    {
        RunInTempDirectory(directory =>
        {
            var executable = Path.Combine(directory, "debug", "WPlayer.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "");

            RunWithTemporaryKey(key =>
            {
                key.SetValue("WPlayer", "existing");

                Assert.IsFalse(StartupRegistration.SetEnabled(key, true, executable));
                Assert.AreEqual("existing", key.GetValue("WPlayer"));
            });
        });
    }

    [TestMethod]
    public void RemovesRegistrationWithoutRequiringInstalledExecutable()
    {
        RunWithTemporaryKey(key =>
        {
            key.SetValue("WPlayer", "existing");

            Assert.IsTrue(StartupRegistration.SetEnabled(key, false, null));
            Assert.IsNull(key.GetValue("WPlayer"));
        });
    }

    [TestMethod]
    public void SettingsShowsStartupRegistrationUnavailable()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig { LaunchAtStartup = false };
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var startup = (CheckBox)settings.FindName("StartupCheckBox");
                var status = (TextBlock)settings.FindName("StatusText");

                startup.IsChecked = true;

                Assert.IsTrue(config.LaunchAtStartup);
                Assert.AreEqual("Startup registration unavailable", status.Text);
                Assert.AreEqual(Visibility.Visible, status.Visibility);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    private static void RunWithTemporaryKey(Action<RegistryKey> action)
    {
        var path = $"Software\\WPlayer.Tests\\{Guid.NewGuid():N}";
        using var key = Registry.CurrentUser.CreateSubKey(path)!;
        try
        {
            action(key);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }

    private static void RunInTempDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"WPlayer.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

}
