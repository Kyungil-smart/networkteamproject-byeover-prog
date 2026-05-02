namespace DeadZone.Actors.UI
{
    [System.Serializable]
    public struct CharacterCustomizeData
    {
        public int bodyIndex;
        public int headIndex;
        public int beardIndex;
        public int hatIndex;

        public CharacterCustomizeData(int bodyIndex, int headIndex, int beardIndex)
            : this(bodyIndex, headIndex, beardIndex, 0)
        {
        }

        public CharacterCustomizeData(int bodyIndex, int headIndex, int beardIndex, int hatIndex)
        {
            this.bodyIndex = bodyIndex;
            this.headIndex = headIndex;
            this.beardIndex = beardIndex;
            this.hatIndex = hatIndex;
        }
    }
}
