using System.Collections.Generic;
using System;

namespace PlayniteControlCenter
{
    public class GameOverlayData
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public int ProcessId { get; set; }
        public DateTime GameStartTime { get; set; }
        public TimeSpan Playtime { get; set; }
        public string CoverImagePath { get; set; }
        public List<AchievementData> Achievements { get; set; }
    }
}