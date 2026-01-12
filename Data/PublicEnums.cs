using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

public static class Panteons
{

    public static List<string> P1 = new()
{
    "GG_Vengefly",
    "GG_Gruz_Mother",
    "GG_False_Knight",
    "GG_Mega_Moss_Charger",
    "GG_Hornet_1",
    "GG_Spa",
    "GG_Ghost_Gorb",
    "GG_Dung_Defender",
    "GG_Mage_Knight",
    "GG_Brooding_Mawlek",
    "GG_Engine",
    "GG_Nailmasters"
};
    public static List<string> P2 = new()
{
    "GG_Ghost_Xero",
    "GG_Crystal_Guardian",
    "GG_Soul_Master",
    "GG_Oblobbles",
    "GG_Mantis_Lords",
    "GG_Spa",
    "GG_Ghost_Marmu",
    "GG_Nosk",
    "GG_Flukemarm",
    "GG_Broken_Vessel",
    "GG_Engine",
    "GG_Painter",
};
    public static List<string> P3 = new()
{
    "GG_Hive_Knight",
    "GG_Ghost_Hu",
    "GG_Collector",
    "GG_God_Tamer",
    "GG_Grimm",
    "GG_Spa",
    "GG_Ghost_Galien",
    "GG_Grey_Prince_Zote",
    "GG_Uumuu",
    "GG_Hornet_2",
    "GG_Engine",
    "GG_Sly"
};
    public static List<string> P4 = new()
{
    "GG_Crystal_Guardian_2",
"GG_Lost_Kin",
"GG_Ghost_No_Eyes",
"GG_Traitor_Lord",
"GG_White_Defender",
"GG_Spa",
"GG_Failed_Champion",
"GG_Ghost_Markoth",
"GG_Watcher_Knights",
"GG_Soul_Tyrant",
"GG_Engine_Prime",
"GG_Hollow_Knight"
};
    public static List<string> P5 = new() {

    "GG_Vengefly_V",
    "GG_Gruz_Mother_V",
    "GG_False_Knight",
    "GG_Mega_Moss_Charger",
    "GG_Hornet_1",
    "GG_Engine",
    "GG_Ghost_Gorb_V",
    "GG_Dung_Defender",
    "GG_Mage_Knight_V",
    "GG_Brooding_Mawlek_V",
    "GG_Nailmasters",
    "GG_Spa",
    "GG_Ghost_Xero_V",
    "GG_Crystal_Guardian",
    "GG_Soul_Master",
    "GG_Oblobbles",
    "GG_Mantis_Lords_V",
    "GG_Spa",
    "GG_Ghost_Marmu_V",
    "GG_Flukemarm",
    "GG_Broken_Vessel",
    "GG_Ghost_Galien",
    "GG_Painter",
    "GG_Spa",
    "GG_Hive_Knight",
    "GG_Ghost_Hu",
    "GG_Collector_V",
    "GG_God_Tamer",
    "GG_Grimm",
    "GG_Spa",
    "GG_Unn",
    "GG_Watcher_Knights",
    "GG_Uumuu_V",
    "GG_Nosk_Hornet",
    "GG_Sly",
    "GG_Hornet_2",
    "GG_Spa",
    "GG_Crystal_Guardian_2",
    "GG_Lost_Kin",
    "GG_Ghost_No_Eyes_V",
    "GG_Traitor_Lord",
    "GG_White_Defender",
    "GG_Spa",
    "GG_Engine_Root",
    "GG_Soul_Tyrant",
    "GG_Ghost_Markoth_V",
    "GG_Grey_Prince_Zote",
    "GG_Failed_Champion",
    "GG_Grimm_Nightmare",
    "GG_Spa",
    "GG_Wyrm",
    "GG_Hollow_Knight",
    "GG_Radiance"
};
   
}
public enum Charm
{
    GatheringSwarm = 1,
    WaywardCompass,
    GrubSong,
    StalwartShell,
    BaldurShell,
    FuryOfTheFallen,
    QuickFocus,
    LifebloodHeart,
    LifebloodCore,
    DefendersCrest,
    Flukenest,
    ThornsOfAgony,
    MarkOfPride,
    SteadyBody,
    HeavyBlow,
    SharpShadow,
    SporeShroom,
    LongNail,
    ShamanStone,
    SoulCatcher,
    SoulEater,
    GlowingWomb,
    UnbreakableHeart,
    UnbreakableGreed,
    UnbreakableStrength,
    NailmastersGlory,
    JonisBlessing,
    ShapeOfUnn,
    Hiveblood,
    Dreamwielder,
    Dashmaster,
    QuickSlash,
    SpellTwister,
    DeepFocus,
    GrubberflysElegy,
    Kingsoul,
    Sprintmaster,
    Dreamshield,
    Weaversong,
    Grimmchild,
    CarefreeMelody,
    MarkOfPurity = 41,
    VesselsLament,
    BoonOfHallownest,
    AbyssalBloom
}
