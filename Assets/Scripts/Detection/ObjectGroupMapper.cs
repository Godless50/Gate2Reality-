namespace Gate2Reality.Detection
{
    using Gate2Reality.Narrative;

    public enum ObjectGroup : byte
    {
        Unknown  = 0,
        Sleep    = 1,
        Food     = 2,
        Movement = 3,
        Child    = 4,
        Light    = 5,
        Sharp    = 6
    }

    public static class ObjectGroupMapper
    {
        public static ObjectGroup GetGroup(NarrativeLabel label) => label switch
        {
            NarrativeLabel.Bed or NarrativeLabel.Couch
                => ObjectGroup.Sleep,
            NarrativeLabel.Table or NarrativeLabel.Cup or NarrativeLabel.Bowl
                or NarrativeLabel.Fork or NarrativeLabel.Bottle
                => ObjectGroup.Food,
            NarrativeLabel.Bicycle or NarrativeLabel.Backpack
                => ObjectGroup.Movement,
            NarrativeLabel.TeddyBear
                => ObjectGroup.Child,
            NarrativeLabel.Tv or NarrativeLabel.Laptop or NarrativeLabel.Phone
                => ObjectGroup.Light,
            NarrativeLabel.Knife or NarrativeLabel.Scissors
                => ObjectGroup.Sharp,
            _ => ObjectGroup.Unknown
        };
    }
}
