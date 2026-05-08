using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public enum LoadingTipCategory
    {
        Control,
        Combat,
        Loot,
        Hideout,
        Extraction,
        EasterEgg,
        Humor
    }

    [Serializable]
    public class LoadingTipEntry
    {
        public LoadingTipCategory category;

        [TextArea(2, 4)]
        public string message;

        public string imageKey;
    }

    [CreateAssetMenu(
        fileName = "LoadingTipDatabase",
        menuName = "DeadZone/UI/Loading Tip Database")]
    public class LoadingTipDatabase : ScriptableObject
    {
        public List<LoadingTipEntry> tips = new();
    }
}
