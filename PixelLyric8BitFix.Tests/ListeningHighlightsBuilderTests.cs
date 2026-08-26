using System;
using System.Collections.Generic;
using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"✨ 查看亮点回顾"背后的 ListeningHighlightsBuilder——封面卡片永远在，其余几张
    /// 各自数据不够就跳过，不硬凑。</summary>
    public class ListeningHighlightsBuilderTests
    {
        [Fact]
        public void BuildSlides_EmptySummary_OnlyHasCoverSlide()
        {
            var summary = new ListeningSummary(); // 全 0/空列表
            var slides = ListeningHighlightsBuilder.BuildSlides(summary, "本月", userName: null);

            Assert.Single(slides);
            Assert.Equal("🎧", slides[0].Icon);
        }

        [Fact]
        public void BuildSlides_CoverHeadline_IncludesUserNameWhenProvided()
        {
            var summary = new ListeningSummary();
            var withName = ListeningHighlightsBuilder.BuildSlides(summary, "本月", "小明");
            var withoutName = ListeningHighlightsBuilder.BuildSlides(summary, "本月", null);

            Assert.Contains("小明", withName[0].Headline);
            Assert.DoesNotContain("小明", withoutName[0].Headline);
        }

        [Fact]
        public void BuildSlides_WithFullData_IncludesAllSixSlides()
        {
            var summary = new ListeningSummary
            {
                TotalSeconds = 36000,
                ActiveDayCount = 10,
                LongestStreakDays = 5,
                UniqueArtistCount = 8,
                UniqueTrackCount = 20,
                BestDay = new DateOnly(2026, 6, 1),
                BestDaySeconds = 7200,
                TopArtists = new List<ArtistStat> { new() { Artist = "艺人甲", Seconds = 5000 } },
                TopTracks = new List<TrackStat> { new() { Title = "歌曲甲", Artist = "艺人甲", Seconds = 3000 } },
            };

            var slides = ListeningHighlightsBuilder.BuildSlides(summary, "今年", "小明");

            Assert.Equal(6, slides.Count);
            Assert.Contains(slides, s => s.Icon == "🎤" && s.Headline == "艺人甲");
            Assert.Contains(slides, s => s.Icon == "🎵" && s.Headline.Contains("歌曲甲"));
            Assert.Contains(slides, s => s.Icon == "🔥");
            Assert.Contains(slides, s => s.Icon == "🌟");
        }

        [Fact]
        public void BuildSlides_NoBestDay_SkipsBestDaySlide()
        {
            var summary = new ListeningSummary { TotalSeconds = 100, ActiveDayCount = 1 };
            var slides = ListeningHighlightsBuilder.BuildSlides(summary, "本月", null);

            Assert.DoesNotContain(slides, s => s.Icon == "🔥");
        }
    }
}
