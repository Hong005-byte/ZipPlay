using System;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class LyricsFetcherTests
    {
        [Fact]
        public void IsDurationPlausible_NoExpectedDuration_AlwaysPasses()
        {
            // 系统没报时长（expectedDuration <= 0）时没法校验，不该拦
            Assert.True(LyricsFetcher.IsDurationPlausible("[03:00.00]end", TimeSpan.Zero));
        }

        [Fact]
        public void IsDurationPlausible_MatchingDuration_Passes()
        {
            string lrc = "[00:00.00]Start\n[03:00.00]End";
            Assert.True(LyricsFetcher.IsDurationPlausible(lrc, TimeSpan.FromMinutes(3)));
        }

        [Fact]
        public void IsDurationPlausible_LastLineWellBeforeRealEnd_Passes()
        {
            // 最后一句歌词离结尾还有一段纯音乐尾奏是正常现象，放宽到 75% 阈值不该被拦
            string lrc = "[00:00.00]Start\n[02:20.00]End"; // 140s / 180s ≈ 0.78
            Assert.True(LyricsFetcher.IsDurationPlausible(lrc, TimeSpan.FromMinutes(3)));
        }

        [Fact]
        public void IsDurationPlausible_ShortVersionAgainstLongTrack_Fails()
        {
            // 抓到的是一分钟的"精简版/电台剪辑"歌词，但实际播放的是三分钟完整版：版本明显不对，应该拒绝
            string lrc = "[00:00.00]Start\n[01:00.00]End";
            Assert.False(LyricsFetcher.IsDurationPlausible(lrc, TimeSpan.FromMinutes(3)));
        }

        [Fact]
        public void IsDurationPlausible_UnparsableLrc_DoesNotBlock()
        {
            // 解析不出最后一句时间戳（没有任何 [mm:ss] 格式），没法校验，别拦
            Assert.True(LyricsFetcher.IsDurationPlausible("not really an lrc file", TimeSpan.FromMinutes(3)));
        }
    }
}
