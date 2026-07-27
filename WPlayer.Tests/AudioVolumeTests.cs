using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class AudioVolumeTests
{
    [TestMethod]
    public void WheelAccumulatesHighResolutionDeltas()
    {
        var wheel = new VolumeWheelAccumulator();

        Assert.AreEqual(0, wheel.Add(30));
        Assert.AreEqual(0, wheel.Add(30));
        Assert.AreEqual(0, wheel.Add(30));
        Assert.AreEqual(1, wheel.Add(30));
    }

    [TestMethod]
    public void WheelReturnsSignedMultipleDetents()
    {
        var wheel = new VolumeWheelAccumulator();

        Assert.AreEqual(2, wheel.Add(240));
        Assert.AreEqual(-1, wheel.Add(-120));
    }

    [TestMethod]
    public void WheelPreservesRemainderAcrossDirectionChangesAndReset()
    {
        var wheel = new VolumeWheelAccumulator();

        Assert.AreEqual(0, wheel.Add(90));
        Assert.AreEqual(0, wheel.Add(-30));
        Assert.AreEqual(-1, wheel.Add(-180));

        wheel.Reset();
        Assert.AreEqual(0, wheel.Add(90));
    }

    [TestMethod]
    public void VolumeMathUsesFivePointStepsAndClamps()
    {
        Assert.AreEqual(0.55f, VolumeMath.ApplyDelta(0.5f, 5), 0.0001f);
        Assert.AreEqual(1f, VolumeMath.ApplyDelta(0.98f, 5));
        Assert.AreEqual(0f, VolumeMath.ApplyDelta(0.02f, -5));
    }

    [TestMethod]
    public void VolumeMathRoundsWholePercentAndTreatsMuteAsZero()
    {
        Assert.AreEqual(56, VolumeMath.ToPercent(0.555f, false));
        Assert.AreEqual(0, VolumeMath.ToPercent(0.8f, true));
    }

    [TestMethod]
    public void SharedAppLevelUsesArithmeticMeanBeforeDelta()
    {
        var first = new FakeAudioVolumeSession(0.2f);
        var second = new FakeAudioVolumeSession(0.6f);

        var result = AudioSessionVolumeBatch.Adjust(new[] { first, second }, 5);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(45, result.Percent);
        Assert.AreEqual(0.45f, first.Level, 0.0001f);
        Assert.AreEqual(0.45f, second.Level, 0.0001f);
    }

    [TestMethod]
    public void MutedWheelDownLeavesTargetUnchanged()
    {
        var session = new FakeAudioVolumeSession(0.8f, muted: true);

        var result = AudioSessionVolumeBatch.Adjust(new[] { session }, -5);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Percent);
        Assert.AreEqual(0.8f, session.Level);
        Assert.IsTrue(session.Muted);
        Assert.AreEqual(0, session.LevelWriteCount);
        Assert.AreEqual(0, session.MuteWriteCount);
    }

    [TestMethod]
    public void MutedWheelUpUnmutesAndAppliesFivePoints()
    {
        var session = new FakeAudioVolumeSession(0.4f, muted: true);

        var result = AudioSessionVolumeBatch.Adjust(new[] { session }, 5);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(45, result.Percent);
        Assert.AreEqual(0.45f, session.Level, 0.0001f);
        Assert.IsFalse(session.Muted);
    }

    [TestMethod]
    public void FailedSessionWriteRestoresEarlierSessions()
    {
        var first = new FakeAudioVolumeSession(0.2f);
        var second = new FakeAudioVolumeSession(0.6f)
        {
            FailNextLevelWrite = true,
            MutateOnFailedWrite = true
        };

        var result = AudioSessionVolumeBatch.Adjust(new[] { first, second }, 5);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0.2f, first.Level, 0.0001f);
        Assert.AreEqual(0.6f, second.Level, 0.0001f);
    }

    [TestMethod]
    public void FailedUnmuteRestoresLevelsAndMuteStates()
    {
        var first = new FakeAudioVolumeSession(0.4f, muted: true);
        var second = new FakeAudioVolumeSession(0.6f, muted: true)
        {
            FailNextMuteWrite = true,
            MutateOnFailedWrite = true
        };

        var result = AudioSessionVolumeBatch.Adjust(new[] { first, second }, 5);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0.4f, first.Level, 0.0001f);
        Assert.AreEqual(0.6f, second.Level, 0.0001f);
        Assert.IsTrue(first.Muted);
        Assert.IsTrue(second.Muted);
    }

    [TestMethod]
    [DataRow(VolumeScrollTarget.CurrentApp, false, (int)AppVolumeLookupStatus.Missing, true)]
    [DataRow(VolumeScrollTarget.CurrentApp, true, (int)AppVolumeLookupStatus.Missing, true)]
    [DataRow(VolumeScrollTarget.CurrentApp, true, (int)AppVolumeLookupStatus.Found, false)]
    [DataRow(VolumeScrollTarget.CurrentApp, true, (int)AppVolumeLookupStatus.Failed, false)]
    [DataRow(VolumeScrollTarget.WindowsMaster, true, (int)AppVolumeLookupStatus.Found, true)]
    public void VolumeTargetPolicyChoosesMaster(
        VolumeScrollTarget preferredTarget,
        bool hasCurrentApp,
        int lookupStatus,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            VolumeTargetPolicy.ShouldUseMaster(preferredTarget, hasCurrentApp, (AppVolumeLookupStatus)lookupStatus));
    }

    [TestMethod]
    public void DesktopAppsMatchNormalizedProcessNames()
    {
        Assert.IsTrue(AudioProcessIdentity.MatchesProcessName("Chrome", "chrome"));
        Assert.IsTrue(AudioProcessIdentity.MatchesProcessName("Spotify.exe", "Spotify"));
    }

    private sealed class FakeAudioVolumeSession(float level, bool muted = false) : IAudioVolumeSession
    {
        public float Level { get; private set; } = level;
        public bool Muted { get; private set; } = muted;
        public bool FailNextLevelWrite { get; set; }
        public bool FailNextMuteWrite { get; set; }
        public bool MutateOnFailedWrite { get; set; }
        public int LevelWriteCount { get; private set; }
        public int MuteWriteCount { get; private set; }

        public bool SetLevel(float newLevel)
        {
            LevelWriteCount++;
            if (FailNextLevelWrite)
            {
                FailNextLevelWrite = false;
                if (MutateOnFailedWrite)
                {
                    Level = newLevel;
                }

                return false;
            }

            Level = newLevel;
            return true;
        }

        public bool SetMuted(bool newMuted)
        {
            MuteWriteCount++;
            if (FailNextMuteWrite)
            {
                FailNextMuteWrite = false;
                if (MutateOnFailedWrite)
                {
                    Muted = newMuted;
                }

                return false;
            }

            Muted = newMuted;
            return true;
        }
    }
}
