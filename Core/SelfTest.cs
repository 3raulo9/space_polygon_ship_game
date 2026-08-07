using System.Numerics;
using Unrendered.Entities;
using Unrendered.Net;
using Unrendered.UI;
using Unrendered.World;

namespace Unrendered.Core;

/// <summary>
/// Headless behaviour check for the combat sim — no window, no graphics. Drives
/// <see cref="World"/> directly and asserts the loop actually works end to end:
/// the player can destroy an enemy, and an enemy can damage the player. Run with
/// `dotnet run -- --selftest`. Exits non-zero on failure so it can gate a build.
/// </summary>
public static partial class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        failures += Check("the world is a finite wrap-around torus", WorldIsAFiniteTorus);
        failures += Check("the skyline is a city, not nine buildings", SkylineIsPopulated);
        failures += Check("the skyline is solid to the craft", StructuresBlockThePlayer);
        failures += Check("rounds stop at a wall and leave it standing", RoundsStopAtTheSkyline);
        failures += Check("a beam cuts a tower down and clears the wreck", BeamsCutStructuresDown);
        failures += Check("a cut fractures locally; the crown falls, the base stands", LocalizedFractureSparesTheBase);
        failures += Check("falling rubble crushes whatever is under it", FallingRubbleCrushesCharacters);
        failures += Check("player can destroy an enemy", PlayerKillsEnemy);
        failures += Check("a kill leaves salvage on the field", KillsLeaveSalvage);
        failures += Check("enemy can damage the player", EnemyDamagesPlayer);
        failures += Check("enemies elevate onto a player in the air", EnemyReachesPlayerInTheAir);
        failures += Check("ammo is finite", AmmoDepletes);
        failures += Check("salvage stows into the inventory, doesn't auto-charge", BatteryStowsThenCharges);
        failures += Check("bullet salvage stows a random handful of rounds", AmmoStowsThenLoads);
        failures += Check("three fragments craft a throwable CRAB CORE", FragmentsCraftCrabCore);
        failures += Check("materials craft rounds and cells", MaterialsCraftRoundsAndCells);
        failures += Check("the bench takes a thing apart into its parts", TeardownYieldsParts);
        failures += Check("each bench takes only what it should", BenchesTakeOnlyWhatTheyShould);
        failures += Check("salvage two players reach at once is not duplicated", SalvageIsSpentWhenTaken);
        failures += Check("a client sees the salvage it is standing in", ClientSeesTheNearestSalvage);
        failures += Check("salvage never lands inside a wall", SalvageNeverLandsInsideAWall);
        failures += Check("a thrown CRAB CORE blast destroys enemies", CrabCoreBlastKills);
        failures += Check("a CRAB CORE blast can destroy the Crab-Core", CrabCoreBlastKillsBoss);
        failures += Check("a CRAB CORE blast can destroy the Maw-Core", CrabCoreBlastKillsMaw);
        failures += Check("grounded shot misses the boss core", GroundedShotMissesCore);
        failures += Check("air shot at core height kills the boss", AirShotKillsCore);
        failures += Check("air shot detonates on the horizon", AirShotExpiresForBlast);
        failures += Check("debug key spawns a random enemy", DebugSpawnAddsEnemy);
        failures += Check("the boss seizes and throws a cornered player", BossSeizesPlayer);
        failures += Check("a held player is raised to face the core", SeizureFramesTheCore);
        failures += Check("the boss's hands frame the held player", SeizureHandsReachThePlayer);
        failures += Check("the boss's beam fires where the player was", BeamLocksItsDirection);
        failures += Check("the maw hangs at the top of the player's jump", MawHangsAtJumpApex);
        failures += Check("only a leaping shot reaches the maw's crystal", MawNeedsAnAirShot);
        failures += Check("the maw swallows a player who stands under it", MawSwallowsStillPlayer);
        failures += Check("three shots from inside break the maw's hold", MawReleasesOnThreeShots);
        failures += Check("digestion bites 15% a time until you escape", MawDigestionBites);
        failures += Check("a swallowed player can shoot their way out for real", MawEscapeThroughTheGun);
        failures += Check("the loadout budget refuses an eleventh point", BudgetRefusesOverspend);
        failures += Check("a 5/5/5 build is exactly the historical craft", DefaultBuildIsTheOldCraft);
        failures += Check("loadout points reach the player's live stats", LoadoutDrivesPlayerStats);
        failures += Check("a solo run that ends puts up an ending, and a match never does", RunOverOpensOnlySolo);
        failures += Check("the ending screen tells the truth about the run", RunOverReadsTheRun);
        failures += Check("shields break one charge at a time, then the hull bleeds", ShieldChargesThenHull);
        failures += Check("a battery buys a charge and a kit buys hull", CellsAndKitsMendDifferentLayers);
        failures += Check("a moon fragment buys all three, or nothing", MoonFragmentRestoresEverything);
        failures += Check("a whole build survives the wire", ABuildSurvivesTheWire);
        failures += Check("the spider's lance costs rounds and burns a line", SpiderLanceKills);
        failures += Check("charging the spider's lance roots the craft", SpiderChargeRootsTheCraft);
        failures += Check("the spider wears its core on the front", SpiderWearsItsCoreOnTheFront);
        failures += Check("a round in the core breaks the wind-up", SpiderCoreHitBreaksTheCharge);
        failures += Check("the meter buys damage, reach and width", SpiderLanceScalesWithTheMeter);
        failures += Check("the claw grabs, crushes and throws", SpiderClawGrabsCrushesAndThrows);
        failures += Check("out of reach, the same trigger is the emitter", SpiderClawFallsBackToTheLaser);
        failures += Check("the legs kick off walls and stand on roofs", SpiderPouncesUpTheCity);
        failures += Check("the mouse turns the whole craft, not a turret", MouseTurnsTheWholeCraft);
        failures += Check("the tank's gun is stopped short of the sky", TankGunElevationIsShallow);
        failures += Check("the spider's gun cranes the full way up", SpiderGunCranesAllTheWay);
        failures += Check("the cannon fires where the craft is aimed", CannonFollowsTheAim);
        failures += Check("a raised tank gun still can't reach the maw's crystal", TankGunStaysUnderTheMaw);
        failures += Check("a tank is too heavy to jump; the spider still hops", TankIsTooHeavyToJump);
        failures += Check("a tank plants to crane its gun and brace", TankPlantsToCraneAndBrace);
        failures += Check("a dug-in tank is too heavy for the crab to seize", PlantedTankResistsSeizure);
        failures += Check("a tank lurches off its tracks for hyper", TankLurchesOffItsTracks);
        failures += Check("a fast hull rams what it drives into", TankRamsWhatItDrivesInto);
        failures += Check("a smoke screen blinds the sight line and eats rounds", TankSmokeBlindsTheField);
        failures += Check("the heavy round lobs like a mortar and comes down", TankMortarLobsAndComesDown);
        failures += Check("a lobbed mortar can bite the Crab-Core", MortarBitesTheCrabCore);
        failures += Check("a lobbed mortar can bite the Maw-Core", MortarBitesTheMawCore);
        failures += Check("the AP slug punches through a whole line", TankSlugPunchesThroughALine);
        failures += Check("armour turns the front and bares the rear", TankArmorTurnsFrontAndRear);
        failures += Check("a soldier opens within a cable's throw of the city", SoldierStartsAtAnAnchor);
        failures += Check("the high jump clears fifteen metres and costs gas", SoldierJumpClearsTheCity);
        failures += Check("a hook bites the building it was aimed at", SoldierHookBites);
        failures += Check("a taut cable swings the player instead of dropping them", SoldierCableSwings);
        failures += Check("releasing at speed keeps every bit of the momentum", SoldierReleaseKeepsMomentum);
        failures += Check("reeling in costs gas and gains speed", SoldierReelBurnsGas);
        failures += Check("rifle rounds fly exactly where the crosshair points", SoldierRifleFliesTrue);
        failures += Check("a felled building stops being an anchor", SoldierAnchorDiesWithItsTower);
        failures += Check("weak material tears out from under a swing", SoldierWeakAnchorTears);
        failures += Check("a squad arrives four strong, hung off a tower", SquadArrivesOnATower);
        failures += Check("the squad calls once and all four come", SquadCallsAndCloses);
        failures += Check("they cross the city on cables, not on jets", SoldiersFlyOnTheirCables);
        failures += Check("one blade at a time, and the turn goes round", SquadStrikesInTurn);
        failures += Check("a soldier is shot out of the air, not off the floor", SoldiersAreHitAtTheirOwnHeight);
        failures += Check("killing one drops the body's salvage", SoldierKillLeavesSalvage);
        failures += Check("a run that connects costs the player shield", SoldierBladesCut);
        failures += Check("cutting a wall drops what was hanging on it", SoldierAnchorDiesWithItsWall);
        failures += Check("a soldier is never a target standing still", SoldiersNeverLoiter);
        failures += Check("the city offers anchors ahead, above and solid", AnchorQueryReadsTheCity);
        failures += Check("a fish opens already swimming, never on the deck", FishStartsSwimming);
        failures += Check("the tail is an impulse, not a throttle", FishBeatIsAnImpulse);
        failures += Check("beats cost breath and only coasting gives it back", FishBreathIsARhythm);
        failures += Check("a rolled body carves tighter than a level one", FishCarveTurnsTighter);
        failures += Check("speed is what holds a fish up", FishLiftHoldsAltitude);
        failures += Check("a strike coils, commits, then leaves you spent", FishStrikeCommits);
        failures += Check("a strike spears one thing and is done", FishStrikeSpearsOnce);
        failures += Check("touching the grid beaches a fish, and beats free it", FishBeachesAndRecovers);
        failures += Check("the bloom warns for ten metres before it bites", FishBloomWarnsFirst);
        failures += Check("the bloom costs shield and thins the water", FishBloomHurts);
        failures += Check("spit flies exactly where the crosshair points", FishSpitFliesTrue);
        failures += Check("a virus opens exposed and flies where it looks", VirusMoteFliesWhereItLooks);
        failures += Check("flying into a hunter wears it as a host", VirusInfectsOnContact);
        failures += Check("a worn host rots out and ejects the mote", VirusHostRots);
        failures += Check("a worn host soaks damage the shield never sees", VirusHostSoaksDamage);
        failures += Check("a naked mote takes hits amplified", VirusMoteIsFragile);
        failures += Check("an overload spends the whole host as a blast", VirusOverloadSpendsTheHost);
        failures += Check("the virus round flies exactly where the crosshair points", VirusRoundFliesTrue);
        failures += Check("an exposed mote withers only after its grace", VirusWithersAfterGrace);
        failures += Check("the crab can be worn, and its lance breaks", VirusWearsTheCrab);
        failures += Check("the maw can be worn, and it hovers", VirusWearsTheMaw);
        failures += Check("a virus round seeds a person rather than only hurting one", VirusSeedsASoldier);
        failures += Check("a seed nobody claims turns them on their squad", SeedRootsIntoACarrier);
        failures += Check("a seeded soldier can be worn, kit and all", VirusWearsASoldier);
        failures += Check("an overload sprays the whole squad", OverloadSpreadsThePlague);
        failures += Check("a carrier with nothing left to fight comes apart", CarrierStarvesOut);
        failures += Check("the grab reaches a body a swing away", VirusLungeCatchesASoldier);
        failures += Check("an exposed mote is blind and passes through walls", ExposedMoteIsIncorporeal);
        failures += Check("a squad does not recognise one of its own", WornSoldierPassesForOneOfThem);
        failures += Check("the squad flies cover for a worn comrade", SquadEscortsTheWornBody);
        failures += Check("an escort shoots the enemy, and holds fire without one", EscortPicksItsFights);
        failures += Check("shooting an escort loses it", BetrayedEscortTurns);
        failures += Check("view shake rings down on every chassis", ShakeAlwaysSettles);
        failures += Check("an input frame survives the wire intact", InputFrameRoundTrips);
        failures += Check("one click is one shot however the loop steps", EdgesAreSpentOnce);
        failures += Check("the pretend wire delays, drops and keeps order", LoopbackCarriesTheTraffic);
        failures += Check("a solo run is still one craft with three lives", SoloIsUnchanged);
        failures += Check("the host seats up to twenty and then refuses", SeatsFillToTheCap);
        failures += Check("revives run out into spectating, not a lost match", RevivesRunOutIntoSpectating);
        failures += Check("friendly fire is the host's toggle, never self-harm", FriendlyFireIsTheHostsCall);
        failures += Check("every seat drives from its own keys", EverySeatDrivesItself);
        failures += Check("hunters chase whoever is nearest them", HuntersChaseTheNearest);
        failures += Check("a spent player is no longer prey", SpectatorsAreNotHunted);
        failures += Check("a round knows which seat fired it", RoundsCarryTheirOwner);
        failures += Check("friendly fire off, a team-mate's round passes through", FriendlyFireOffSparesTheTeam);
        failures += Check("friendly fire on, a team-mate's round bites", FriendlyFireOnHurtsTheTeam);
        failures += Check("a snapshot survives the wire and lands where it was sent", SnapshotRoundTrips);
        failures += Check("a client sees the host's craft move, over a bad wire", ClientTracksTheHost);
        failures += Check("latency does not drag a client's own craft backwards", PredictionIsNotDraggedBackwardsByLatency);
        failures += Check("a player's own aim is never wrenched back by the wire", ClientAimIsNeverWrenchedBack);
        failures += Check("one press is one shot, even with the packet lost", ALostInputPacketCostsNoActionAndDuplicatesNone);
        failures += Check("a remote craft coasts through a lost packet, it doesn't stall", ARemoteCraftCoastsThroughALostPacket);
        failures += Check("a client's own craft obeys the host's harm and capture", OwnCraftObeysTheHost);
        failures += Check("a remote craft glides between snapshots, it doesn't strobe", RemoteCraftInterpolates);
        failures += Check("a boss's seizure crosses so onlookers see the grab", SeizureArmCrossesTheWire);
        failures += Check("debris thrown on the host is seen on the client", EffectCrossesTheWire);
        failures += Check("a join code decodes back to the host who read it out", JoinCodesRoundTrip);
        failures += Check("the handshake completes on the lobby screen alone", LobbyHandshakeSeatsAJoiner);
        failures += Check("a pick in the room installs that chassis on both the client and the host", PickInstallsChosenChassis);
        failures += Check("a sound raised on the host is heard on the client, positioned", SoundCrossesTheWire);
        failures += Check("a client no longer simulates the field it is only shown", ClientDoesNotSimulateTheField);
        failures += Check("the host's bosses cross the wire as client puppets", BossesCrossTheWire);
        failures += Check("salvage on the host is drawn on the client", PickupsCrossTheField);
        failures += Check("a snapshot carries each seat's chassis, and the client rebuilds it", ChassisCrossesTheWire);
        failures += Check("two players open close enough to see each other", SeatsOpenWithinSight);
        failures += Check("a client grows its roster to see a later, higher-seated joiner", ClientGrowsForLaterSeats);
        failures += Check("a lost field packet keeps the enemies it had", FieldPacketIsKeepLast);
        failures += Check("a dropped player is held, then restored on rejoin", DropAndRejoinRestoresTheSeat);
        failures += Check("a rejoined player's controls still reach the host", ARejoinedPlayerCanStillDrive);
        failures += Check("a full match refuses a new joiner but not a rejoiner", FullMatchStillLetsYouBack);
        failures += Check("the five worlds are five different places", TheFiveWorldsAreDifferentPlaces);
        failures += Check("a world's gravity and density reach the sim", APlanetsConditionsReachTheSim);
        failures += Check("night on SOLUNE closes the fog and the hunters in", NightOnSoluneClosesIn);
        failures += Check("the destination and the mode survive the wire", RulesSurviveTheWire);
        failures += Check("the world's clock crosses to a client", TheHoursCrossesTheWire);
        failures += Check("a room's vote settles on the most-wanted world", TheRoomVoteSettles);
        failures += Check("enemies off empties PLANET but keeps the city", EnemiesOffEmptiesThePlanet);
        failures += Check("a worn host, and its rot, cross the wire", VirusHostCrossesTheWire);
        failures += Check("a tower cut down on the host comes down on the client", StructureDamageCrossesTheWire);
        failures += Check("a razed lot stays razed for a client that arrives late", RazedLotsReachALateClient);
        failures += Check("salvage is personal: each craft keeps what it drove over", SalvageIsPerSeat);
        failures += Check("a client's inventory move is the host's to make", InventoryIntentsAreHostAuthoritative);
        failures += Check("a seat's pack survives the wire intact", InventoryCrossesTheWire);
        failures += Check("which metal falls out of a cell is the host's roll", BreakingIsTheHostsRoll);
        failures += Check("a thrown item leaves the pack and lands on the grid", ThrowingOneLandsItOnTheField);
        failures += Check("a client's throw is the host's to place", ThrowingIsHostAuthoritative);
        failures += Check("a spent player watches a living team-mate", SpentPlayerSpectatesASurvivor);
        failures += Check("cables and stolen lances cross to onlookers", RigsCrossTheWire);
        failures += Check("a laggy client's shot is scored where they saw it", LagCompensationRewindsTheTarget);
        failures += Check("a remote player's cable leaves their own body", RigTriggersAreSeatAware);
        failures += Check("a remote tank's smoke screen hides that tank", MachineKitsAreSeatAware);
        failures += Check("the city is solid to every craft, not just this one", WallsAreSolidForEverySeat);
        failures += Check("squads hunt whoever is nearest them", SquadsHuntEverySeat);
        failures += Check("falling rubble crushes any player under it", CrushBillsEverySeat);
        failures += Check("the field fills around every player, not just the host", SpawnsFollowEverySeat);

        // --- A room with more than two people in it -------------------------------
        // Everything above this line was written against a host and one client, which is the
        // one shape of session that was never actually broken.
        failures += Check("five players all see each other as the craft they picked", EveryoneSeesEveryChassis);
        failures += Check("a craft owns its build, so nobody is redrawn as somebody else", CraftBuildIsNotShared);
        failures += Check("a hello sent into a socket that isn't up yet is repeated", HelloSurvivesADeadSocket);
        failures += Check("one player leaving does not end everybody else's match", AHostOutlivesItsPlayers);
        failures += Check("a seat given up in the lobby is handed to the next joiner", AbandonedSeatsAreReused);
        failures += Check("a full match tells the joiner so instead of ignoring them", AFullMatchRefusesOutLoud);
        failures += Check("somebody who joins a running match still picks their craft", LateJoinerPicksTheirChassis);
        failures += Check("a player who arrives after LAUNCH is still named on every screen", NamesReachEveryoneAfterLaunch);
        failures += Check("the points and paint a player spent reach every screen", ABuildReachesEveryone);
        failures += Check("a rules change reaches the seat count clients grow by", RulesReachTheClientsWorld);

        // --- The room ---------------------------------------------------------------
        // Twenty players is a room, not twenty simultaneous single-player games. These are
        // the things that make it one, and every last one of them is about whether something
        // done on one machine reached another.
        failures += Check("a kill is credited and announced to the whole room", KillsAreCreditedAndAnnounced);
        failures += Check("the scoreboard and the ping column cross the wire", TheScoreboardCrossesTheWire);
        failures += Check("a mark one player drops is seen by the others", MarksReachTheWholeRoom);
        failures += Check("a spectator can choose who they watch", ASpectatorCanChangeWhoTheyWatch);

        // --- The sound engine ------------------------------------------------------
        // Pure float arithmetic, so all of it runs with no audio device at all. This is
        // how the mix is checked without two machines and a pair of ears.
        failures += Check("a sound gets quieter and duller the further off it is", DistanceDullsAndQuietens);
        failures += Check("a sound to the right comes out of the right speaker", PanFollowsTheView);
        failures += Check("a tower between you and a shot muffles it", TheCityMufflesWhatIsBehindIt);
        failures += Check("a distant blast arrives after the flash", SoundTakesTimeToArrive);
        failures += Check("twenty rifles do not become forty voices", InstanceCapsHoldTheLine);
        failures += Check("a loud close sound displaces a quiet far one", LoudNearbyBeatsQuietFarOff);
        failures += Check("a boss death is never dropped for a footstep", PriorityProtectsTheBigMoments);
        failures += Check("the mix never leaves the rails", TheLimiterHoldsTheCeiling);
        failures += Check("a blast in your face muffles the whole world", ConcussionDucksAndDulls);
        failures += Check("a spectator hears from the craft they are riding", EarsRideTheCamera);
        failures += Check("a sound over the seam is heard beside you", TheTorusDoesNotBreakTheEars);
        failures += Check("the world's noise fades out but the menu keeps its voice", WorldFadeSilencesTheWorldNotTheUi);

        // --- The monsters can reach anybody, not just seat 0 ----------------------
        failures += Check("the crab's beam burns whoever is standing in it", BeamBurnsEverySeat);
        failures += Check("the crab seizes whoever it corners, not only the host", SeizureTakesEverySeat);
        failures += Check("the maw swallows whoever stands under it", MawSwallowsEverySeat);
        failures += Check("the maw's lasers bite every craft they reach", MawLasersBiteEverySeat);
        failures += Check("one player being seized does not disarm the rest", ASeizedMateDoesNotFreezeTheRoom);
        failures += Check("a splash round bites whoever is standing in it", SplashBitesEverySeat);

        // --- Controls and the mixer -----------------------------------------------
        failures += Check("the shipped bindings are the controls the game always had",
            DefaultBindingsMatchTheOldHardcodedKeys);
        failures += Check("a rebound control survives being written and read back",
            BindingsRoundTripThroughTheConfig);
        failures += Check("two actions on one button are flagged, and only where it matters",
            ClashesAreReportedWithinASectionOnly);
        failures += Check("the controls list fits the screen wherever it is scrolled to",
            TheControlsListAlwaysFitsOnScreen);
        failures += Check("every cue is on a fader the player can actually reach",
            EveryCueIsOnACategoryBus);
        failures += Check("a category fader turns down its own cues and nobody else's",
            CategoryFadersAreIndependent);

        // The figure. Everything here is true over time rather than in any one frame, which
        // is exactly why none of it can be checked by looking at the game.
        failures += Check("a body in the air folds harder the faster it goes", FlightFoldsWithSpeed);
        failures += Check("an arrival buckles the knees and springs back up",
            LandingCompressesThenRecovers);
        failures += Check("a limb arrives after the body it hangs from",
            LimbsLagTheBodyTheyHangFrom);
        failures += Check("a planted boot stays put while the body walks over it",
            PlantedBootsDoNotSkate);
        failures += Check("the boots stay on their own sides of the body",
            BootsStayOnTheirOwnSides);
        failures += Check("the shoulders turn against the hips through a stride",
            HipsAndShouldersTurnAgainstEachOther);
        failures += Check("a hand goes to the cable that is carrying it",
            AHeldCablePutsTheHandOnItsLauncher);
        failures += Check("a hit throws the head away from whatever caused it",
            FlinchThrowsTheHeadAwayFromTheHit);
        failures += Check("no run of nonsense can put a NaN in a joint",
            NothingProducesANonNumber);

        // DESCENT: the run director, the rolled bosses and the seam that keeps the mode from
        // quietly becoming SANDBOX with a bar over it. Its own block so the output reads as one.
        failures += RunDescentChecks();

        // --- The way off a planet ---------------------------------------------------
        // The fragments, the gates, the panel, the portal, and the crossing that strings
        // five planets into one session.
        failures += RunArchChecks();

        // --- The chat and its console -----------------------------------------------
        failures += RunChatChecks();

        // The FLOWER: a chassis made almost entirely of refusals, none of which a screenshot
        // can tell apart from a broken one. Its own block, same reasoning as above.
        failures += RunFlowerChecks();

        // And whether the six chassis can fight each other at all, which is the one thing a
        // solo run can never show and a room shows immediately. Its own block, last, because
        // it stands up a fresh two-seat world per attack and is the slowest thing in the file.
        failures += RunDuelChecks();

        Console.WriteLine(failures == 0
            ? "SELFTEST: all checks passed"
            : $"SELFTEST: {failures} check(s) FAILED");
        return failures == 0 ? 0 : 1;
    }

    // --- The skyline -----------------------------------------------------------

    /// <summary>
    /// The field's rejection sampler is asked for hundreds of buildings and quietly
    /// settles for whatever fits, so a spacing change that over-subscribes the torus
    /// doesn't fail — it just returns a near-empty world that still runs and still
    /// renders, and nobody notices until they look at the horizon. This pins a floor
    /// under it. The numbers are a long way below what the field is asked for, so
    /// ordinary retuning doesn't trip it and a collapse does.
    /// </summary>
    private static string? SkylineIsPopulated()
    {
        var field = StructureField.Create();
        int towers = 0, arcs = 0;
        foreach (var s in field)
        {
            if (s.Kind == StructureKind.Tower) towers++; else arcs++;
        }

        if (towers < 40) return $"only {towers} towers were placed";
        if (arcs < 6) return $"only {arcs} arcs were placed";

        // And nothing standing on the ground every screen opens on — the craft's start,
        // the hangar's turntable and the title screen's idling camera all live in here.
        foreach (var s in field)
            if (s.Position.Length() < StructureField.ClearRadius)
                return $"a structure stands {s.Position.Length():0.0} from the origin";

        return null;
    }

    /// <summary>
    /// A tower is a wall, not scenery: driving into one has to stop the craft entering
    /// it, and — just as importantly — has to leave the craft somewhere legal rather
    /// than jammed on the surface it was pushed out of.
    /// </summary>
    private static string? StructuresBlockThePlayer()
    {
        var world = new World.World();
        var tower = FirstTower(world);
        if (tower == null) return "no tower to drive into";

        // Park the craft inside the footprint and let one tick resolve it.
        world.Player.Position = tower.Position;
        StepWithoutInput(world);

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        tower.Blockers(blockers);
        float wanted = blockers[0].Radius + Entities.PlayerTank.Radius;
        float got = Torus.Distance(world.Player.Position, blockers[0].At);
        if (got < wanted - 0.01f)
            return $"craft sits {got:0.00} into a footprint that reaches {wanted:0.00}";

        // Steady state: another hundred ticks of the same resolution must not creep the
        // craft anywhere, which is what would happen if the push-out overshot and the
        // next tick pushed it back.
        Vector2 settled = world.Player.Position;
        for (int i = 0; i < 100; i++) StepWithoutInput(world);
        if (Torus.Distance(settled, world.Player.Position) > 0.01f)
            return "the craft drifts while resting against a wall";

        return null;
    }

    /// <summary>
    /// A round stops against a wall whoever fired it, and — just as load-bearing — the
    /// wall is still standing afterwards. Bullets are what buildings are <em>for</em>;
    /// only a beam takes one down, which the next check covers.
    /// </summary>
    private static string? RoundsStopAtTheSkyline()
    {
        var world = new World.World { DynamicSpawning = false };
        var tower = FirstTower(world);
        if (tower == null) return "no tower to shoot at";

        // Stand off the tower and aim square at it. Well outside its footprint, so the
        // round has grid to cross and is genuinely stopped rather than born inside a wall.
        Vector2 delta = Torus.Delta(tower.Position, world.Player.Position);
        Vector2 away = Vector2.Normalize(delta) * 14f;
        world.Player.Position = Torus.Wrap(tower.Position + away);
        world.Player.Heading = MathF.Atan2(-away.X, -away.Y);

        world.FirePlayerShot();
        for (int i = 0; i < 120; i++)
        {
            StepWithoutInput(world);
            if (!AnyProjectileActive(world)) break;
        }

        if (AnyProjectileActive(world)) return "the round never stopped";
        if (tower.Falling) return "a plain round brought a tower down";

        // And it stopped *at the wall*, not by flying past and expiring: something has to
        // still be standing between the craft and where it was aiming.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        if (tower.Blockers(blockers) == 0) return "the tower stopped being solid";

        return null;
    }

    /// <summary>
    /// A beam is not a round: the SPIDER's charged lance cuts a tower down and runs the
    /// collapse to completion, after which the field has genuinely let it go.
    /// </summary>
    private static string? BeamsCutStructuresDown()
    {
        var loadout = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(loadout) { DynamicSpawning = false };
        var tower = FirstTower(world);
        if (tower == null) return "no tower to cut";
        if (world.Player.Spider == null) return "the spider chassis has no emitter";

        Vector2 delta = Torus.Delta(tower.Position, world.Player.Position);
        Vector2 away = Vector2.Normalize(delta) * 20f;
        world.Player.Position = Torus.Wrap(tower.Position + away);
        world.Player.Heading = MathF.Atan2(-away.X, -away.Y);

        // Wind the lance to full and loose it down the line of the tower.
        for (int i = 0; i < 200; i++) world.Player.Spider.Hold((float)Config.FixedDt);
        world.FireSpiderLanceForTest();

        if (!tower.Falling) return "the lance left the tower standing";

        // The collapse has to actually finish and clear itself off the field, or a run
        // long enough accumulates wreckage that is neither solid nor ever removed. The
        // count is only checked for having dropped, not for having dropped by one: the
        // lance is ninety units long and deliberately fells everything standing in it,
        // so a shot that opens a road through three towers is correct behaviour.
        int before = world.Structures.Count;
        for (int i = 0; i < 60 * 6; i++) StepWithoutInput(world);
        if (world.Structures.Contains(tower)) return "the wreck never left the field";
        if (world.Structures.Count >= before) return "nothing was cleared off the field";

        return null;
    }

    private static bool AnyProjectileActive(World.World world)
    {
        foreach (var p in world.Projectiles) if (p.Active) return true;
        return false;
    }

    private static Structure? FirstTower(World.World world)
    {
        foreach (var s in world.Structures)
            if (s.Kind == StructureKind.Tower) return s;
        return null;
    }

    // --- The hangar: the loadout budget and the SPIDER chassis ------------------

    private static string? BudgetRefusesOverspend()
    {
        var lo = new Loadout();

        // A track can only climb into points that are actually free, and the opening 5/5/5/5
        // spread spends the budget exactly — so a track cannot climb at all until something
        // else is sold down. That the climb stalls until you do is itself the rule under test.
        if (lo.Adjust(Loadout.Stat.Shield, +1))
            return $"shield climbed to {lo.Shield} with the budget already spent";

        while (lo.Adjust(Loadout.Stat.Speed, -1)) { }
        while (lo.Adjust(Loadout.Stat.Ammo, -1)) { }
        while (lo.Adjust(Loadout.Stat.Shield, +1)) { }
        if (lo.Shield != Loadout.StatMax) return $"shield capped at {lo.Shield}, want 10";

        // Ten in shields, one apiece in speed and ammo and the untouched five in hull is
        // seventeen of twenty, so the fourth track can take the last three and stop dead.
        while (lo.Adjust(Loadout.Stat.Health, +1)) { }
        if (lo.Health != 8) return $"the fourth track reached {lo.Health}, want 8";

        if (lo.Adjust(Loadout.Stat.Speed, +1))
            return $"a track climbed to {lo.Speed} with the budget spent";
        if (lo.Speed != Loadout.StatMin) return $"speed sits at {lo.Speed}, want 1";
        if (lo.Spent != Loadout.Budget) return $"spent {lo.Spent}, want {Loadout.Budget}";

        // ...and the even spread is legal, spending the lot exactly.
        var even = new Loadout();
        if (even.Spent != Loadout.Budget || even.Remaining != 0)
            return $"5/5/5/5 spends {even.Spent} of {Loadout.Budget}";
        return null;
    }

    private static string? DefaultBuildIsTheOldCraft()
    {
        var lo = new Loadout();
        // The historical 100 points of punishment, now split down the middle: five charges of
        // ten in front of fifty of hull. Adding the fourth track was not allowed to make
        // anybody tougher, and this is the assertion that says so.
        if (MathF.Abs(lo.MaxShield - 50f) > 0.01f) return $"shield {lo.MaxShield}, want 50";
        if (MathF.Abs(lo.MaxHealth - 50f) > 0.01f) return $"hull {lo.MaxHealth}, want 50";
        if (MathF.Abs(lo.MaxShield + lo.MaxHealth - 100f) > 0.01f)
            return $"a flat build soaks {lo.MaxShield + lo.MaxHealth}, want the historical 100";
        if (lo.ShieldCharges != 5) return $"{lo.ShieldCharges} charges, want 5";
        if (MathF.Abs(lo.SpeedScale - 1f) > 0.001f) return $"speed scale {lo.SpeedScale}, want 1";
        if (lo.MaxAmmo != 50) return $"magazine {lo.MaxAmmo}, want 50";
        return null;
    }

    private static string? LoadoutDrivesPlayerStats()
    {
        var lo = new Loadout();
        while (lo.Adjust(Loadout.Stat.Ammo, +1)) { }        // ammo to 10, the rest starve
        var world = new World.World(lo);

        if (world.Player.MaxAmmo != lo.MaxAmmo)
            return $"magazine {world.Player.MaxAmmo}, loadout says {lo.MaxAmmo}";
        if (MathF.Abs(world.Player.MaxShield - lo.MaxShield) > 0.01f)
            return $"shield {world.Player.MaxShield}, loadout says {lo.MaxShield}";
        if (MathF.Abs(world.Player.TopSpeed - PlayerTankMaxSpeed * lo.SpeedScale) > 0.01f)
            return $"top speed {world.Player.TopSpeed} doesn't match the speed track";
        if (world.Player.Ammo > world.Player.MaxAmmo)
            return "opened with more rounds than the magazine holds";
        return null;
    }

    /// <summary>
    /// When the ending screen is allowed to appear. Both endings of a solo run earn it — the
    /// craft spent, and a DESCENT cleared — and a networked match never does, whatever happens
    /// to the craft at this keyboard. Nineteen other people are still playing.
    /// </summary>
    private static string? RunOverOpensOnlySolo()
    {
        var solo = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        solo.Enemies.Clear();
        if (RunOverScreen.ShouldOpen(solo, networked: false))
            return "a live run was called over before anything happened to it";

        // Spent: the run is over. Every life, not one — a solo run opens with the match's
        // revives on it and a craft with a comeback left is not finished.
        for (int i = 0; i < 12 && solo.Player.Alive; i++) SpendALife(solo.Player);
        if (!solo.Player.Spectating) return "the test failed to spend the craft";
        if (!RunOverScreen.ShouldOpen(solo, networked: false))
            return "a spent solo craft did not end the run";

        // The identical world in a match ends nothing — this is the guard that matters, since
        // the loop's only other cue that it is in a match is the session being non-null.
        if (RunOverScreen.ShouldOpen(solo, networked: true))
            return "a match put an ending screen over one player's death";

        // ...and clearing a world does NOT end a run, which is the half that changed when the
        // arch was built. A Colossus going down used to be the end of DESCENT; it is the middle
        // now — the arches take power and the crossing carries on — and a panel appearing over
        // a player who has just been handed three light columns and somewhere to go would take
        // the run away from them at the exact moment it opened up.
        var cleared = new World.World(null,
            new MatchSettings { MaxPlayers = 1, Mode = GameMode.Descent }, new Campaign())
        { DynamicSpawning = false };
        if (cleared.Run is null) return "a DESCENT world opened with no run on it";
        if (RunOverScreen.ShouldOpen(cleared, networked: false))
            return "a descent was called over at the landing";
        cleared.Run.SkipTo(DescentPhase.Cleared, Descent.WaveCount, cleared);
        if (RunOverScreen.ShouldOpen(cleared, networked: false))
            return "clearing a world ended the run instead of opening the arches";
        if (cleared.Player.Spectating) return "the test cleared the world by dying, which proves nothing";

        // A LOST descent still does, though — that is the one automatic ending left.
        var beaten = new World.World(null,
            new MatchSettings { MaxPlayers = 1, Mode = GameMode.Descent }, new Campaign())
        { DynamicSpawning = false };
        beaten.Run!.SkipTo(DescentPhase.Lost, 3, beaten);
        if (!RunOverScreen.ShouldOpen(beaten, networked: false))
            return "a lost descent did not end the run";
        return null;
    }

    /// <summary>
    /// What the panel says. A win must not be dressed as a death — different heading, different
    /// first row — and the run's own numbers have to be the ones it reports.
    /// </summary>
    private static string? RunOverReadsTheRun()
    {
        var lost = new World.World(new Loadout { Class = PlayerClass.Fish },
            MatchSettings.SinglePlayer)
        { DynamicSpawning = false };
        lost.Enemies.Clear();
        for (int i = 0; i < 12 && lost.Player.Alive; i++) SpendALife(lost.Player);

        var screen = new RunOverScreen();
        screen.Open(lost);
        if (screen.Won) return "a spent craft was reported as a win";
        if (screen.RetryLabel != "TRY AGAIN") return $"a loss offered '{screen.RetryLabel}'";
        if (screen.Selected != RunOverScreen.Row.Retry)
            return "the ending did not open on the row a player most likely wants";
        // The craft it names is the craft that was flown, off Build (what the renderer reads),
        // not the stale Class copy that has drifted from it before.
        if (!Named(screen, "CRAFT", "FISH")) return "the ending named the wrong chassis";

        // A cleared DESCENT: the other face of the same screen.
        var won = new World.World(null, new MatchSettings { MaxPlayers = 1, Mode = GameMode.Descent })
        { DynamicSpawning = false };
        if (won.Run is null) return "a DESCENT world opened with no run on it";
        won.Run.SkipTo(DescentPhase.Cleared, Descent.WaveCount, won);
        screen.Open(won);
        if (!screen.Won) return "a cleared world was reported as a death";
        if (screen.RetryLabel != "GO AGAIN") return $"a win offered '{screen.RetryLabel}'";
        if (!screen.Title.Contains("CLEAR")) return $"a win was headed '{screen.Title}'";
        if (!Named(screen, "REACHED", "ALL FIVE WAVES"))
            return "a cleared run did not say it had gone all the way down";

        // Every row leads somewhere, and no two lead to the same place.
        var seen = new HashSet<RunOverScreen.Action>();
        foreach (var row in System.Enum.GetValues<RunOverScreen.Row>())
        {
            if (screen.LabelOf(row).Length == 0) return $"row {row} has no label";
            if (screen.HintOf(row).Length == 0) return $"row {row} explains nothing";
            screen.SelectForTest(row);
            if (!seen.Add(RowAction(row))) return $"two rows do the same thing ({row})";
        }
        if (seen.Count != RunOverScreen.RowCount) return "a row leads nowhere";

        // The clock is read as a length of time, not as a float of seconds.
        if (RunOverScreen.Clock(0f) != "0:00") return "a run of no time read as " + RunOverScreen.Clock(0f);
        if (RunOverScreen.Clock(125f) != "2:05") return "125 seconds read as " + RunOverScreen.Clock(125f);
        return null;
    }

    private static RunOverScreen.Action RowAction(RunOverScreen.Row row) => row switch
    {
        RunOverScreen.Row.Retry => RunOverScreen.Action.Retry,
        RunOverScreen.Row.Hangar => RunOverScreen.Action.Hangar,
        _ => RunOverScreen.Action.Menu,
    };

    /// <summary>Whether the ending's readout carries a given label with a given value.</summary>
    private static bool Named(RunOverScreen screen, string label, string value)
    {
        foreach (var (l, v) in screen.Lines)
            if (l == label) return v == value;
        return false;
    }

    /// <summary>
    /// The two-layer damage model, end to end: charges are counted off one at a time, a hit
    /// bigger than the charge it breaks spills into the next, nothing reaches the hull while
    /// any charge stands, and only the hull running out spends a life.
    /// </summary>
    private static string? ShieldChargesThenHull()
    {
        var lo = new Loadout();                       // 5 charges of 10, 50 of hull
        var world = new World.World(lo, new MatchSettings { Revives = 3 });
        PlayerTank p = world.Player;

        if (p.ChargesLeft != 5) return $"opened on {p.ChargesLeft} charges, want 5";

        // A hit inside one charge breaks nothing yet — it is not spent until it is empty.
        p.TakeDamage(6f);
        if (p.ChargesLeft != 5) return $"a partial hit popped a charge ({p.ChargesLeft} left)";
        if (p.TopChargeFraction > 0.45f) return "the top charge did not read as bitten into";

        // ...and the next four points finish it.
        p.TakeDamage(4f);
        if (p.ChargesLeft != 4) return $"an emptied charge did not break ({p.ChargesLeft} left)";

        // A big hit spills: 25 is two and a half charges, and the half lands on the third.
        p.TakeDamage(25f);
        if (p.ChargesLeft != 2) return $"a 25-point hit left {p.ChargesLeft} charges, want 2";
        if (MathF.Abs(p.Health - p.MaxHealth) > 0.001f)
            return "damage reached the hull with charges still standing";

        // Strip the rest of the shield exactly, and the hull is still untouched.
        p.TakeDamage(p.Shield);
        if (p.ChargesLeft != 0) return "the last charge survived a hit worth all of it";
        if (MathF.Abs(p.Health - p.MaxHealth) > 0.001f)
            return "the hull took the overkill of a hit that exactly emptied the shield";
        int lives0 = p.Lives;
        if (lives0 != world.Match.Revives + 1) return "the match's revives did not reach the craft";

        // NOW it bleeds, and only now.
        p.TakeDamage(10f);
        if (MathF.Abs(p.Health - (p.MaxHealth - 10f)) > 0.001f)
            return $"hull at {p.Health} after a 10-point hit through a spent shield";
        if (p.Lives != lives0) return "a hull scratch cost a life";

        // And running the hull out is what spends one — which brings the whole craft back.
        p.TakeDamage(p.Health);
        if (p.Lives != lives0 - 1) return "an emptied hull did not spend a life";
        if (p.ChargesLeft != 5 || p.Health < p.MaxHealth - 0.001f)
            return "a revive did not rebuild the craft whole";
        return null;
    }

    /// <summary>
    /// The two consumables mend the two layers and neither substitutes for the other: a cell
    /// puts back exactly one charge and never touches hull, a kit mends hull and never puts a
    /// charge back.
    /// </summary>
    private static string? CellsAndKitsMendDifferentLayers()
    {
        var world = new World.World(new Loadout(), new MatchSettings { Revives = 3 });
        PlayerTank p = world.Player;

        // A craft that has been through it: the shields stripped and the hull opened, then
        // two charges put back on the stack. Both layers hurt, which is the only state where
        // the two items can be told apart by what they do.
        p.TakeDamage(p.MaxShield + 12f);
        p.ChargeShield(2);
        if (p.ChargesLeft != 2) return $"the setup left {p.ChargesLeft} charges, want 2";
        float hurtHull = p.Health;
        if (hurtHull >= p.MaxHealth) return "the setup failed to wound the hull";

        // A cell: one charge, and the hull is not its business.
        var inv = world.Inventory;
        inv.Slots[0] = new ItemStack(ItemKind.Battery, 1);
        if (!world.ChargeFromSlot(p, inv, 0)) return "a cell refused to be spent on a hurt craft";
        if (p.ChargesLeft != 3) return $"one cell left {p.ChargesLeft} charges, want 3";
        if (MathF.Abs(p.Health - hurtHull) > 0.001f) return "a cell mended the hull";

        // A kit: hull, and the shields are not its business.
        int charges = p.ChargesLeft;
        inv.Slots[1] = new ItemStack(ItemKind.RepairKit, 1);
        if (!world.ChargeFromSlot(p, inv, 1)) return "a kit refused to be spent on a hurt hull";
        if (p.Health <= hurtHull) return "a kit did not mend the hull";
        if (p.ChargesLeft != charges) return "a kit put a shield charge back";

        // Neither is wasted on a craft that does not need it.
        p.Shield = p.MaxShield;
        p.Health = p.MaxHealth;
        p.Hyper = p.MaxHyper;
        inv.Slots[2] = new ItemStack(ItemKind.Battery, 1);
        inv.Slots[3] = new ItemStack(ItemKind.RepairKit, 1);
        if (world.ChargeFromSlot(p, inv, 2)) return "a full craft still swallowed a cell";
        if (world.ChargeFromSlot(p, inv, 3)) return "an unhurt hull still swallowed a kit";
        return null;
    }

    /// <summary>
    /// The moon fragment is the one item that does all three at once — every charge, the whole
    /// hull, the full reserve — and, exactly like the cell and the kit, refuses to be spent on a
    /// craft that needs none of it.
    ///
    /// <para>The refusal is the half worth checking. This is the rarest object in the game and
    /// the only one a player might carry across a whole run waiting for the right moment; an
    /// item that strong being silently swallowed by a full craft on a mis-click is not a
    /// balance problem, it is the run.</para>
    /// </summary>
    private static string? MoonFragmentRestoresEverything()
    {
        var world = new World.World(new Loadout(), new MatchSettings { Revives = 3 });
        PlayerTank p = world.Player;

        // A craft in genuine trouble on all three counts at once, which nothing else in the
        // game can answer in one action.
        p.TakeDamage(p.MaxShield + 18f);
        p.Hyper = 5f;
        if (p.ChargesLeft != 0) return "the setup left the shield standing";
        if (p.Health >= p.MaxHealth) return "the setup failed to wound the hull";

        var inv = world.Inventory;
        inv.Slots[0] = new ItemStack(ItemKind.Moonstone, 1);
        if (!world.ChargeFromSlot(p, inv, 0)) return "a fragment refused a craft that needed it";

        if (p.ChargesLeft != p.ShieldCharges)
            return $"the shield came back to {p.ChargesLeft} of {p.ShieldCharges} charges";
        if (p.Health < p.MaxHealth) return "the hull was not made whole";
        if (p.Hyper < p.MaxHyper) return "the reserve was not filled";
        if (!inv.Slots[0].IsEmpty) return "the fragment was not spent";

        // And it is refused outright by a craft with nothing missing.
        inv.Slots[1] = new ItemStack(ItemKind.Moonstone, 1);
        if (world.ChargeFromSlot(p, inv, 1)) return "a whole craft still swallowed a fragment";
        if (inv.Slots[1].IsEmpty) return "a refused fragment was spent anyway";

        // It round-trips through the grid like everything else a pack can hold: thrown out and
        // picked back up, it is still a moon fragment and not a handful of rounds.
        if (World.World.SalvageOf(ItemKind.Moonstone) != PickupKind.Moonstone
            || World.World.ItemOf(PickupKind.Moonstone) != ItemKind.Moonstone)
            return "a fragment did not survive being thrown on the grid";
        return null;
    }

    /// <summary>
    /// A build is not private to the machine that made it: chassis, all four tracks and every
    /// part's paint have to survive being written down and read back, and a packet that claims
    /// more than the budget allows has to come back as a craft the hangar could have made.
    /// </summary>
    private static string? ABuildSurvivesTheWire()
    {
        var lo = new Loadout { Class = PlayerClass.Fish };
        while (lo.Adjust(Loadout.Stat.Speed, -1)) { }
        while (lo.Adjust(Loadout.Stat.Health, +1)) { }
        lo.CycleSwatch(PlayerClass.Fish, 0, +3);
        lo.CycleSwatch(PlayerClass.Fish, 2, +1);

        Span<byte> buf = stackalloc byte[Loadout.Bytes];
        lo.Write(buf);
        Loadout back = Loadout.Read(buf);

        if (back.Class != PlayerClass.Fish) return $"the chassis arrived as {back.Class}";
        for (int i = 0; i < Loadout.StatCount; i++)
            if (back[(Loadout.Stat)i] != lo[(Loadout.Stat)i])
                return $"track {(Loadout.Stat)i} arrived as {back[(Loadout.Stat)i]}, sent {lo[(Loadout.Stat)i]}";
        for (int part = 0; part < ClassCatalog.Get(PlayerClass.Fish).PartNames.Length; part++)
            if (back.SwatchIndex(PlayerClass.Fish, part) != lo.SwatchIndex(PlayerClass.Fish, part))
                return $"part {part}'s paint did not survive the wire";
        if (!lo.SameAs(back)) return "a build did not recognise its own round trip";

        // A hand-written packet asking for four maxed tracks is walked back to something legal
        // rather than trusted — the host replays these, and a craft nobody could build in the
        // hangar must not be buildable by typing.
        Span<byte> cheat = stackalloc byte[Loadout.Bytes];
        lo.Write(cheat);
        for (int i = 0; i < Loadout.StatCount; i++) cheat[1 + i] = 10;
        Loadout clamped = Loadout.Read(cheat);
        if (clamped.Spent > Loadout.Budget)
            return $"an over-budget packet bought a craft spending {clamped.Spent}";
        return null;
    }

    private static float PlayerTankMaxSpeed => Entities.PlayerTank.MaxSpeed;

    private static string? SpiderLanceKills()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo);
        if (world.Player.Spider is not { } spider) return "spider chassis has no emitter";

        world.Enemies.Clear();
        // Two hunters strung out along the craft's forward axis (+Z at heading 0), so a
        // single shaft has to rake through both — the lance pierces, it doesn't stop at
        // the first thing it touches.
        var near = new Entities.EnemyTank(new Vector2(0f, 20f), elite: false);
        var far = new Entities.EnemyTank(new Vector2(0f, 40f), elite: false);
        world.Enemies.Add(near);
        world.Enemies.Add(far);

        int ammo0 = world.Player.Ammo;

        // Wind the meter to full, then let go. Both calls go through the world's own
        // trigger handler, so this exercises the same path a held right-click does.
        for (int i = 0; i < 200 && spider.Charge < Entities.SpiderWeapon.MaxCharge; i++)
            spider.Hold((float)Config.FixedDt);
        world.FireSpiderLanceForTest();

        if (near.Alive || far.Alive)
            return $"lance left hunters standing (near {near.Alive}, far {far.Alive})";
        if (world.Player.Ammo >= ammo0)
            return "a full-charge lance cost no rounds";
        if (spider.Charge != 0f) return $"meter kept {spider.Charge} after firing";
        return null;
    }

    private static string? SpiderChargeRootsTheCraft()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo);
        if (world.Player.Spider is not { } spider) return "spider chassis has no emitter";

        // Root the craft and shove it: a rooted craft ignores drive input, so with no
        // momentum carried in it must not travel. (StepWithoutInput doesn't press
        // anything anyway — what's under test is that Rooted survives a step and that
        // the charge climbs while it's set.)
        world.Player.Rooted = true;
        Vector2 start = world.Player.Position;
        for (int i = 0; i < 30; i++)
        {
            spider.Hold((float)Config.FixedDt);
            StepWithoutInput(world);
        }

        if (Vector2.Distance(world.Player.Position, start) > 0.01f)
            return "a rooted craft drifted while charging";
        if (spider.Charge <= 0f) return "the meter never filled";
        if (spider.Charge > Entities.SpiderWeapon.MaxCharge)
            return $"the meter overran its ceiling ({spider.Charge})";
        return null;
    }

    // --- The machines: TANK and SPIDER aim ---------------------------------------

    /// <summary>
    /// The mouse turns the whole craft — the thing the player asked for after a turret
    /// that swung the view one way while A/D swung the body another read as disorienting.
    /// A yaw of half a radian has to move the shared heading by exactly that, on both
    /// machines, with nothing left held off to the side.
    /// </summary>
    private static string? MouseTurnsTheWholeCraft()
    {
        foreach (var kind in new[] { PlayerClass.Tank, PlayerClass.Spider })
        {
            var p = new Entities.PlayerTank(Vector2.Zero, heading: 0.7f,
                loadout: new Loadout { Class = kind });
            if (!p.IsMachine) return $"{kind} isn't treated as a machine";

            float before = p.Heading;
            p.Look(0.5f, 0f);
            if (MathF.Abs(p.Heading - (before + 0.5f)) > 1e-4f)
                return $"{kind}: a 0.5 rad mouse yaw moved the heading {p.Heading - before:0.000}";
            // And the craft's forward — what it drives and fires along — comes round with it.
            var want = new Vector2(MathF.Sin(before + 0.5f), MathF.Cos(before + 0.5f));
            if (Vector2.Distance(p.Forward, want) > 1e-4f)
                return $"{kind}: forward didn't follow the turned heading";
        }
        return null;
    }

    /// <summary>
    /// The tank's gun is stopped short of the sky — fifteen degrees either way. This is
    /// the number that keeps the Maw-Core honest (see <see cref="TankGunStaysUnderTheMaw"/>),
    /// so the elevation clamp is checked here in isolation before the shot that depends on it.
    /// </summary>
    private static string? TankGunElevationIsShallow()
    {
        var p = new Entities.PlayerTank(Vector2.Zero);

        for (int i = 0; i < 200; i++) p.Look(0f, 0.1f);    // crane hard up
        if (p.Pitch > Entities.PlayerTank.TurretElevation + 1e-4f)
            return $"the gun elevated to {p.Pitch:0.00}, past its {Entities.PlayerTank.TurretElevation:0.00} stop";
        if (p.Pitch < Entities.PlayerTank.TurretElevation - 1e-4f)
            return $"the gun stalled at {p.Pitch:0.00} short of its stop";
        if (p.GunElevation > Entities.PlayerTank.TurretElevation + 1e-4f)
            return "the shot's elevation runs past the gun's stop";

        for (int i = 0; i < 200; i++) p.Look(0f, -0.1f);   // and hard down
        if (p.Pitch < -Entities.PlayerTank.TurretElevation - 1e-4f)
            return $"the gun depressed to {p.Pitch:0.00}, past its stop";
        return null;
    }

    /// <summary>
    /// A tank does not jump — treads have no answer to gravity, and the class stopped
    /// pretending otherwise. The refusal has to be clean: no lift, and not a drop of
    /// the reserve spent, because a bar that drains for nothing reads as a bug rather
    /// than a limit. The same press on the SPIDER must still work, so what is checked
    /// is the TANK's own refusal and not a regression in the machine hop.
    /// </summary>
    private static string? TankIsTooHeavyToJump()
    {
        var tank = new Entities.PlayerTank(Vector2.Zero);
        float hyper0 = tank.Hyper;
        if (tank.TryJump()) return "the tank agreed to jump";
        if (tank.Hyper < hyper0) return "the refused jump still spent hyper";
        if (tank.IsAirborne) return "the refusal left the tank airborne";

        var spider = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });
        if (!spider.TryJump()) return "the spider lost its hop too";
        if (spider.Hyper >= spider.MaxHyper) return "the spider's hop cost no hyper";
        return null;
    }

    /// <summary>
    /// The SPIDER's ring, unlike the tank's gun, cranes the full way up — nearly to
    /// vertical, the Crab-Core's own reach. It pays for that with an exposed core, not by
    /// being stopped short.
    /// </summary>
    private static string? SpiderGunCranesAllTheWay()
    {
        var p = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });

        for (int i = 0; i < 400; i++) p.Look(0f, 0.1f);
        if (p.Pitch < Entities.PlayerTank.MaxPitch - 0.05f)
            return $"the spider's ring only craned to {p.Pitch:0.00}, not near vertical";
        if (p.GunElevation < Entities.PlayerTank.MaxPitch - 0.05f)
            return "the spider's shot elevation is capped below its look";
        return null;
    }

    /// <summary>
    /// The cannon leaves along the craft's heading, which the mouse aims: turn the craft
    /// and the bolt turns with it. If the shot went along a stale facing, the free look
    /// would be a lie the first round exposes.
    /// </summary>
    private static string? CannonFollowsTheAim()
    {
        var p = new Entities.PlayerTank(Vector2.Zero, heading: 0.3f);
        p.Look(0.8f, 0f);   // swing the craft round with the mouse

        if (!p.TryFire(out _, out Vector2 dir, out _))
            return "the cannon refused to fire";

        if (Vector2.Distance(Vector2.Normalize(dir), p.Forward) > 1e-3f)
            return "the bolt left along something other than the aimed heading";
        return null;
    }

    /// <summary>
    /// The load-bearing limit. The Maw-Core hangs its crystal at the top of a jump so the
    /// class has to leave the ground to hurt it; the tank's new elevation must not quietly
    /// hand it an anti-air gun that snipes the gem from the deck. So the gun's own stop is
    /// checked to be genuinely shallow, and — the real test — a bolt fired up it from the
    /// grid is walked its whole life and must never once pass through the crystal's strike
    /// band while it is anywhere near the maw's column.
    /// </summary>
    private static string? TankGunStaysUnderTheMaw()
    {
        var p = new Entities.PlayerTank(Vector2.Zero);
        // Crane the gun as far up as it goes, harder than any hand could.
        for (int i = 0; i < 200; i++) p.Look(0f, 0.1f);
        if (p.GunElevation > Entities.PlayerTank.TurretElevation + 1e-4f)
            return $"the gun elevated to {p.GunElevation:0.00}, past its {Entities.PlayerTank.TurretElevation:0.00} stop";

        // Stand the maw out along the shot and fire up the raised gun. Walk the round its
        // whole flight; if it ever satisfies HitsCrystal, a grounded tank could kill it.
        float band = Entities.MawRig.CrystalWorldY;
        for (float range = 8f; range <= 60f; range += 2f)
        {
            var maw = new Entities.MawCore(new Vector2(0f, range));
            var round = new Entities.Projectile();
            // Straight down +Z, up the gun's own elevation, from the barrel.
            round.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: 0,
                launchHeight: Entities.Projectile.BoltHeight, pitch: p.GunElevation);
            for (int i = 0; i < 240 && round.Active; i++)
            {
                round.Update((float)Config.FixedDt);
                if (maw.HitsCrystal(round.Position, round.Height))
                    return $"a grounded tank bolt reached the crystal at range {range:0} "
                         + $"(height {round.Height:0.0}, band {band:0.0})";
            }
        }
        return null;
    }

    // --- The TANK's siege kit ---------------------------------------------------
    // Everything the heavy chassis gained when it gave up the jump. Each mechanic is driven
    // through the same public entry points the live world uses, so the checks exercise the
    // real trigger paths rather than a headless copy of them.

    private static World.World TankWorld()
    {
        // The default loadout is the TANK on a straight 5/5/5, which is exactly the craft
        // these checks want. Spawning off and the field cleared so the seeded scene is known.
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    /// <summary>
    /// The siege plant. Rolling, the tank's gun is stopped short — the number that keeps the
    /// Maw-Core honest. Dug in, it cranes the full way up (the tank's answer to a game it can no
    /// longer leave the ground to solve), and standing back up re-clamps the gun so a shot on
    /// the way out can't leave at an angle the moving chassis can't hold. No other chassis plants.
    /// </summary>
    private static string? TankPlantsToCraneAndBrace()
    {
        var p = TankWorld().Player;

        if (MathF.Abs(p.LookElevation - Entities.PlayerTank.TurretElevation) > 1e-4f)
            return "a rolling tank's gun wasn't at its shallow stop";

        if (!p.TogglePlant() || !p.Planted) return "the tank refused to plant";
        if (MathF.Abs(p.LookElevation - Entities.PlayerTank.MaxPitch) > 1e-4f)
            return "a planted tank's gun didn't crane the full way up";

        for (int i = 0; i < 200; i++) p.Look(0f, 0.1f);   // crane hard up
        if (p.Pitch <= Entities.PlayerTank.TurretElevation + 0.01f)
            return $"the planted gun stalled at {p.Pitch:F2}, no higher than rolling";

        p.TogglePlant();
        if (p.Planted) return "the tank wouldn't stand back up";
        if (p.Pitch > Entities.PlayerTank.TurretElevation + 1e-4f)
            return $"the gun stayed craned at {p.Pitch:F2} after standing up";

        var spider = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });
        spider.TogglePlant();
        if (spider.Planted) return "a non-tank chassis planted";
        return null;
    }

    /// <summary>
    /// The plant's other half of the bargain: dug in, the tank is too anchored for the Crab-Core
    /// to lift. It gave up the airborne escape from the grab, so planting is the escape the air
    /// used to be — paid for by being unable to move a metre while it holds.
    /// </summary>
    private static string? PlantedTankResistsSeizure()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        if (!Entities.CrabSeizure.CanSeize(boss, player))
            return "the boss wouldn't seize a grounded tank at point-blank";

        if (!player.TogglePlant()) return "the cornered tank refused to plant";
        if (Entities.CrabSeizure.CanSeize(boss, player))
            return "the crab seized a dug-in tank it should be too heavy to lift";
        return null;
    }

    /// <summary>
    /// The lurch: a Hyper-fed track-boost dodge that throws the hull far further than a drive
    /// could in the same breath. Refused while dug in, and owned by no other chassis.
    /// </summary>
    private static string? TankLurchesOffItsTracks()
    {
        var world = TankWorld();
        var p = world.Player;
        p.Heading = 0f;
        Vector2 start = p.Position;
        float hyper0 = p.Hyper;

        if (!p.TryLurch()) return "a tank on a full reserve refused to lurch";
        if (p.Hyper >= hyper0) return "the lurch cost no hyper";

        for (int i = 0; i < 24; i++) StepWithoutInput(world);   // ~0.4s: run the surge out
        float moved = Torus.Distance(p.Position, start);
        if (moved < 10f) return $"the lurch only carried the hull {moved:F1} units";

        if (!p.TogglePlant()) return "the tank refused to plant";
        if (p.TryLurch()) return "a planted tank lurched anyway";
        p.TogglePlant();

        var spider = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });
        if (spider.TryLurch()) return "a non-tank chassis lurched";
        return null;
    }

    /// <summary>
    /// The ram: a hull genuinely driving into a hunter crushes it. Built up to ramming speed
    /// with a lurch straight at a parked hunter — the slam erases it, where a stationary tank
    /// would simply sit there.
    /// </summary>
    private static string? TankRamsWhatItDrivesInto()
    {
        var world = TankWorld();
        var p = world.Player;
        p.Heading = 0f;   // faces +Z
        world.Enemies.Add(new Entities.EnemyTank(p.Position + new Vector2(0f, 4f), elite: false));

        if (!p.TryLurch()) return "the tank wouldn't lurch to build ramming speed";
        for (int i = 0; i < 20 && world.Enemies.Count > 0; i++) StepWithoutInput(world);

        return world.Enemies.Count == 0
            ? null : "the hull drove through a hunter without crushing it";
    }

    /// <summary>
    /// The smoke dischargers. A laid screen breaks the line of sight from a shooter to the
    /// player and swallows a round sitting in the murk — and the dischargers won't re-fire
    /// until they cool, so the screen is rationed rather than a wall the tank hides behind
    /// forever.
    /// </summary>
    private static string? TankSmokeBlindsTheField()
    {
        var world = TankWorld();
        var p = world.Player;
        p.Heading = 0f;
        Vector2 shooter = p.Position + new Vector2(0f, 20f);

        if (world.SmokeBlocks(shooter, p.Position))
            return "the sight line was blocked before any smoke was laid";

        if (!world.DeploySmokeForTest())
            return "the dischargers refused to fire on a full cooldown";
        for (int i = 0; i < 40; i++) StepWithoutInput(world);   // let the screen bloom

        if (!world.SmokeBlocks(shooter, p.Position))
            return "a bloomed screen didn't break the sight line";
        if (!world.SmokeAbsorbs(p.Position))
            return "a round sitting in the screen wasn't absorbed";
        if (world.DeploySmokeForTest())
            return "the dischargers re-fired with no cooldown";
        return null;
    }

    /// <summary>
    /// The mortar. The heavy round is no longer a flat slug: it lobs, climbing well above head
    /// height and coming back down far downrange — indirect fire that clears low cover and the
    /// hunters massed in front of a target. Watched over its whole flight: it has to peak high
    /// and vanish on the grid a long way out, not skim the plane and fizzle.
    /// </summary>
    private static string? TankMortarLobsAndComesDown()
    {
        var world = TankWorld();
        var p = world.Player;
        p.Heading = 0f;
        Vector2 start = p.Position;

        world.FirePlayerGrenade();

        float apex = 0f, lastDist = 0f, lastHeight = 0f;
        bool sawShell = false, landed = false;
        for (int i = 0; i < 200; i++)
        {
            Entities.Projectile? shell = null;
            foreach (var pr in world.Projectiles)
                if (pr.Active && pr.IsGrenade) { shell = pr; break; }

            if (shell != null)
            {
                sawShell = true;
                apex = MathF.Max(apex, shell.Height);
                lastDist = Torus.Distance(shell.Position, start);
                lastHeight = shell.Height;
            }
            else if (sawShell) { landed = true; break; }

            StepWithoutInput(world);
        }

        if (!sawShell) return "no mortar shell was ever in the air";
        if (apex < 5f) return $"the heavy round didn't lob — peaked at {apex:F1}m";
        if (!landed) return "the shell never came down or expired";
        if (lastDist < 40f) return $"the shell fell only {lastDist:F1} units out — no real lob";
        if (lastHeight > 2f) return $"the shell vanished at height {lastHeight:F1}, not on the grid";
        return null;
    }

    /// <summary>
    /// The mortar is the tank's indirect answer to a boss it can no longer leap up to hit: a lob
    /// that comes down on or arcs across the Crab-Core's raised gem strikes it directly, for a
    /// couple of good drops rather than one. A dormant boss is planted a mortar's throw ahead and
    /// shelled until its core gives out.
    /// </summary>
    private static string? MortarBitesTheCrabCore()
    {
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Player.Ammo = world.Player.MaxAmmo;
        world.SpawnCrabAhead();   // a dormant Crab-Core out along the player's heading

        for (int i = 0; i < 60 * 20 && world.Boss is { Alive: true }; i++)
        {
            if (!AnyGrenadeAloft(world)) world.FirePlayerGrenade();
            StepWithoutInput(world);
        }
        return world.Boss is null or { Alive: false }
            ? null : "mortars never brought the Crab-Core down";
    }

    /// <summary>The same, against the Maw-Core's hovering crystal — the weak point every other
    /// tank shot has to leap for. A lobbed mortar reaches it from the grid.</summary>
    private static string? MortarBitesTheMawCore()
    {
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Player.Ammo = world.Player.MaxAmmo;
        world.SpawnMawAhead();

        for (int i = 0; i < 60 * 20 && world.Maw is { Alive: true }; i++)
        {
            if (!AnyGrenadeAloft(world)) world.FirePlayerGrenade();
            StepWithoutInput(world);
        }
        return world.Maw is null or { Alive: false }
            ? null : "mortars never brought the Maw-Core down";
    }

    private static bool AnyGrenadeAloft(World.World world)
    {
        foreach (var pr in world.Projectiles)
            if (pr.Active && pr.IsGrenade) return true;
        return false;
    }

    /// <summary>
    /// The AP slug: a heavy round that punches straight through a whole line of hunters instead
    /// of stopping at the first, at the cost of a fistful of the magazine. Three hunters strung
    /// out along the gun line all die to one slug, and the magazine is docked exactly its cost.
    /// </summary>
    private static string? TankSlugPunchesThroughALine()
    {
        var world = TankWorld();
        var p = world.Player;
        p.Heading = 0f;
        p.Ammo = p.MaxAmmo;
        int ammo0 = p.Ammo;

        world.Enemies.Add(new Entities.EnemyTank(p.Position + new Vector2(0f, 6f), elite: false));
        world.Enemies.Add(new Entities.EnemyTank(p.Position + new Vector2(0f, 10f), elite: false));
        world.Enemies.Add(new Entities.EnemyTank(p.Position + new Vector2(0f, 14f), elite: false));

        world.FirePlayerSlug();
        for (int i = 0; i < 30 && world.Enemies.Count > 0; i++) StepWithoutInput(world);

        if (world.Enemies.Count > 0)
            return $"{world.Enemies.Count} of 3 hunters survived a piercing slug";
        if (ammo0 - p.Ammo != Entities.PlayerTank.SlugAmmoCost)
            return $"the slug cost {ammo0 - p.Ammo} rounds, expected {Entities.PlayerTank.SlugAmmoCost}";
        return null;
    }

    /// <summary>
    /// Directional armour: the tank's sloped glacis turns a frontal shot, its flanks take a hit
    /// square, and its thin rear plate takes it worse — so which way the hull faces when a round
    /// arrives is the whole difference. Planting hardens the front further, and none of the three
    /// bodies has plating at all.
    /// </summary>
    private static string? TankArmorTurnsFrontAndRear()
    {
        var tank = new Entities.PlayerTank(Vector2.Zero);   // faces +Z at heading 0
        float front = tank.ArmorMultiplierFromShot(new Vector2(0f, -1f));  // arriving from the front
        float flank = tank.ArmorMultiplierFromShot(new Vector2(1f, 0f));   // crossing the flank
        float rear = tank.ArmorMultiplierFromShot(new Vector2(0f, 1f));    // arriving from behind

        if (!(front < flank)) return $"the front ({front:F2}) didn't turn a shot better than the flank ({flank:F2})";
        if (!(rear > flank)) return $"the rear ({rear:F2}) wasn't softer than the flank ({flank:F2})";
        if (MathF.Abs(flank - 1f) > 1e-4f) return $"a flank hit should land in full, was {flank:F2}";

        tank.TogglePlant();
        float planted = tank.ArmorMultiplierFromShot(new Vector2(0f, -1f));
        if (!(planted < front)) return $"planting ({planted:F2}) didn't harden the front ({front:F2})";
        tank.TogglePlant();

        var soldier = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Soldier });
        if (MathF.Abs(soldier.ArmorMultiplierFromShot(new Vector2(0f, -1f)) - 1f) > 1e-4f)
            return "a body had directional armour";
        return null;
    }

    /// <summary>
    /// The SPIDER's plating is the tank's read backwards, which is what the class-select
    /// screen has always claimed and what the chassis never did: the core is on the front,
    /// so a round into it bills <em>more</em>, and the shell and the legs turn one aside.
    /// Bracing a lance hardens everything except the face the emitter fires out of.
    /// </summary>
    private static string? SpiderWearsItsCoreOnTheFront()
    {
        var spider = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });
        if (spider.Spider is not { } emitter) return "spider chassis has no emitter";

        var fromFront = new Vector2(0f, -1f);
        var fromSide = new Vector2(1f, 0f);
        var fromBehind = new Vector2(0f, 1f);

        float core = spider.ArmorMultiplierFromShot(fromFront);
        float flank = spider.ArmorMultiplierFromShot(fromSide);
        float back = spider.ArmorMultiplierFromShot(fromBehind);

        if (!(core > 1f)) return $"a shot into the core billed {core:F2}, expected worse than full";
        if (!(flank < 1f)) return $"the shell didn't turn a flanking shot ({flank:F2})";
        if (!(back < flank)) return $"the carapace's back ({back:F2}) wasn't the hardest face";
        if (!spider.StruckInTheCore(fromFront)) return "a frontal round didn't count as a core hit";
        if (spider.StruckInTheCore(fromBehind)) return "a round from behind counted as a core hit";

        // Braced: the meter is winding, the legs are planted and the shell is up.
        emitter.Hold(0.2f);
        float bracedFlank = spider.ArmorMultiplierFromShot(fromSide);
        float bracedCore = spider.ArmorMultiplierFromShot(fromFront);
        if (!(bracedFlank < flank)) return $"bracing didn't harden the flank ({bracedFlank:F2} vs {flank:F2})";
        if (MathF.Abs(bracedCore - core) > 1e-4f)
            return "bracing covered the core, which is the one thing it must not do";
        return null;
    }

    /// <summary>
    /// A round that finds the core while the lance is winding takes the wind with it: the
    /// meter empties, the emitter is dead for a beat, and holding the trigger through the
    /// lockout gets nothing. A hit anywhere else leaves the charge alone — that is what
    /// bracing is for.
    /// </summary>
    private static string? SpiderCoreHitBreaksTheCharge()
    {
        var spider = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });
        if (spider.Spider is not { } emitter) return "spider chassis has no emitter";

        // A hit on the shell while winding: the charge survives it.
        emitter.Hold(1f);
        float wound = emitter.Charge;
        if (spider.StruckInTheCore(new Vector2(1f, 0f)))
            return "a flanking round counted as a core hit";
        if (MathF.Abs(emitter.Charge - wound) > 1e-4f) return "a flank hit disturbed the meter";

        // And one into the core: gone, and locked out.
        if (!emitter.Break()) return "a core hit didn't break a live charge";
        if (emitter.Charge != 0f) return $"the meter kept {emitter.Charge} through a break";
        if (!emitter.Broken) return "the emitter wasn't locked out after a break";

        emitter.Hold((float)Config.FixedDt);
        if (emitter.Charging || emitter.Charge > 0f)
            return "the emitter wound up again while it was still locked out";

        // The lockout runs down on the weapon's own clock.
        for (int i = 0; i < 120 && emitter.Broken; i++) emitter.Update((float)Config.FixedDt);
        if (emitter.Broken) return "the break lockout never cleared";
        emitter.Hold((float)Config.FixedDt);
        if (!emitter.Charging) return "the emitter never came back after its lockout";

        // A break takes the charge outright; an interruption that isn't a shot stows it.
        var other = new Entities.SpiderWeapon();
        other.Hold(1f);
        float stowed = other.Charge;
        other.Cancel();
        other.Hold((float)Config.FixedDt);
        if (other.Charge < stowed)
            return $"a stowed charge came back at {other.Charge}, less than the {stowed} put down";
        return null;
    }

    /// <summary>
    /// The claw: it closes on a hunter in reach, carries it out in front of the core,
    /// crushes it while it is in there, and throws it. A held body stops driving, stops
    /// shooting, and slows the craft carrying it.
    /// </summary>
    private static string? SpiderClawGrabsCrushesAndThrows()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo) { DynamicSpawning = false };
        if (world.Player.Claw is not { } claw) return "spider chassis has no claw";

        world.Enemies.Clear();
        world.Player.Heading = 0f;   // faces +Z

        // Out in front, inside arm's reach.
        var prey = new Entities.EnemyTank(
            world.Player.Position + new Vector2(0f, Entities.SpiderClaw.Reach - 1f), elite: false);
        world.Enemies.Add(prey);

        float loose = world.Player.TopSpeed;
        if (!claw.TryGrab(prey)) return "the claw refused a hunter standing inside its reach";
        if (!prey.Grabbed) return "a caught hunter didn't know it was caught";
        if (!(world.Player.TopSpeed < loose))
            return $"carrying a hunter didn't slow the craft ({world.Player.TopSpeed} vs {loose})";

        // The brain is off while it is up there: a stepped hunter neither drives nor fires.
        Vector2 wasAt = prey.Position;
        if (prey.Update((float)Config.FixedDt, world.Player.Position, 0f, out _, out _, out _))
            return "a hunter fired from inside the claw";
        if (Vector2.Distance(prey.Position, wasAt) > 1e-4f) return "a held hunter drove itself";

        // Squeezing drains it, and the hold parks it in front of the craft.
        float shield0 = prey.Shield;
        for (int i = 0; i < 30; i++)
        {
            float bite = claw.Hold((float)Config.FixedDt,
                world.Player.Position + world.Player.Forward * Entities.SpiderClaw.HoldReach,
                Entities.SpiderClaw.HoldHeight, world.Player.Heading);
            prey.TakeDamage(bite);
        }
        if (!(prey.Shield < shield0)) return "the squeeze didn't cost the held body anything";
        if (prey.Height <= 0f) return "a held hunter was still standing on the grid";

        // It shields the core: a round arriving from the front is the hostage's problem.
        if (!claw.ShieldsFrom(new Vector2(0f, -1f), world.Player.Forward))
            return "the held body didn't cover a round arriving from the front";
        if (claw.ShieldsFrom(new Vector2(0f, 1f), world.Player.Forward))
            return "the held body covered a round arriving from behind";

        // And the throw: it leaves the hand, and it leaves it travelling.
        var thrown = claw.Throw(new Vector3(0f, 0f, 1f));
        if (!ReferenceEquals(thrown, prey)) return "the throw didn't hand back what was held";
        if (claw.Holding) return "the claw was still full after a throw";
        if (prey.Grabbed) return "a thrown hunter still thought it was held";
        if (!prey.Flung || prey.Toss.Z <= 0f) return "a thrown hunter wasn't going anywhere";

        // The world flies it and lands it.
        for (int i = 0; i < 60 * 4 && prey.Flung; i++) world.StepForTest((float)Config.FixedDt);
        if (prey.Flung) return "a thrown hunter never came down";
        if (prey.Alive && prey.Height != 0f) return "a landed hunter didn't come back to the grid";
        return null;
    }

    /// <summary>
    /// The other half of the trigger: with nothing in reach the same button is the
    /// emitter, and it costs exactly what the tank's cannon costs. The two never overlap,
    /// which is the whole reason they fit on one button.
    /// </summary>
    private static string? SpiderClawFallsBackToTheLaser()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo) { DynamicSpawning = false };
        if (world.Player.Claw is not { } claw) return "spider chassis has no claw";

        world.Enemies.Clear();
        world.Player.Heading = 0f;
        world.Player.Ammo = world.Player.MaxAmmo;

        // A hunter well out of arm's reach — the range the emitter is for.
        var far = new Entities.EnemyTank(
            world.Player.Position + new Vector2(0f, Entities.SpiderClaw.Reach + 25f), elite: false);
        world.Enemies.Add(far);

        int ammo0 = world.Player.Ammo;
        world.FirePlayerShot(laser: true);
        if (world.Player.Ammo != ammo0 - 1) return "a laser didn't cost exactly one round";
        if (claw.Holding) return "the claw grabbed something a whole street away";

        // And it gets there: the round is fast and flat, so a hunter twenty-five units out
        // is dead well inside a second.
        for (int i = 0; i < 60 && far.Alive; i++)
        {
            world.StepForTest((float)Config.FixedDt);
            if (far.Alive) world.FirePlayerShot(laser: true);
        }
        if (far.Alive) return "a line of lasers left a hunter standing at open range";
        return null;
    }

    /// <summary>
    /// The legs: a kick off a wall carries the craft up, costs the reserve, and — chained
    /// — puts it on top of the city, where it stands on the roof rather than falling
    /// through it.
    /// </summary>
    private static string? SpiderPouncesUpTheCity()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo) { DynamicSpawning = false };
        world.Enemies.Clear();

        // Stand the craft against the nearest tower, just outside its footprint.
        World.Structure? tower = null;
        foreach (var s in world.Structures)
        {
            if (s.Kind != World.StructureKind.Tower) continue;
            tower = s;
            break;
        }
        if (tower is null) return "the city has no towers to climb";

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[World.Structure.MaxBlockers];
        int n = tower.Blockers(blockers);
        if (n == 0) return "the tower has no footprint";
        var (wall, radius) = blockers[0];

        world.Player.Position = wall + new Vector2(radius + Entities.PlayerTank.Radius + 1f, 0f);
        world.Player.Height = 0f;
        world.Player.Hyper = world.Player.MaxHyper;

        float hyper0 = world.Player.Hyper;
        if (!world.Player.TryPounce(new Vector2(1f, 0f))) return "the legs refused a wall in reach";
        if (!(world.Player.Hyper < hyper0)) return "a pounce cost nothing";

        for (int i = 0; i < 20; i++) world.StepForTest((float)Config.FixedDt);
        if (!(world.Player.Height > 0f)) return "a pounce didn't get the craft off the grid";

        // A craft parked on a roof stands on it: grounded, at the parapet's height, and not
        // shoved off the side by the wall pass. Dropped onto it from a little above with no
        // momentum carried in, which is what arriving at the top of a climb looks like —
        // still rising off the last kick it would legitimately read as airborne, because it
        // would be.
        world.Player.ResetMomentum();
        world.Player.Position = wall;
        world.Player.Height = tower.BlockHeight + 0.4f;
        for (int i = 0; i < 40; i++) world.StepForTest((float)Config.FixedDt);

        if (MathF.Abs(world.Player.Height - tower.BlockHeight) > 0.5f)
            return $"a craft on a roof fell to {world.Player.Height}, expected {tower.BlockHeight}";
        if (world.Player.IsAirborne)
            return $"a craft standing on a roof read as airborne "
                 + $"(at {world.Player.Height}, floor {world.Player.GroundHeight})";
        if (Torus.Delta(wall, world.Player.Position).Length() > radius)
            return "the wall pass shoved a craft off the roof it was standing on";
        return null;
    }

    /// <summary>
    /// The meter buys three things at once now — damage, reach and width — so a full lance
    /// visibly is a bigger beam rather than the same beam with a bigger number. And it
    /// costs half what it used to, because the root was always the real price.
    /// </summary>
    private static string? SpiderLanceScalesWithTheMeter()
    {
        var weak = new Entities.SpiderWeapon();
        var full = new Entities.SpiderWeapon();

        weak.Hold(Entities.SpiderWeapon.MinCharge / Entities.SpiderWeapon.ChargeRate);
        for (int i = 0; i < 200 && full.Charge < Entities.SpiderWeapon.MaxCharge; i++)
            full.Hold((float)Config.FixedDt);

        if (!(full.Damage > weak.Damage)) return "a full meter didn't hit harder";
        if (!(full.AmmoCost > weak.AmmoCost)) return "a full meter didn't cost more";
        if (full.AmmoCost > Entities.SpiderWeapon.MaxBeamAmmo)
            return $"a full lance billed {full.AmmoCost}, over its own ceiling";

        float shortReach = Entities.SpiderWeapon.LengthAt(weak.ChargeFraction);
        float longReach = Entities.SpiderWeapon.LengthAt(full.ChargeFraction);
        if (!(longReach > shortReach)) return "the meter didn't buy any reach";
        if (!(Entities.SpiderWeapon.RadiusAt(1f) > Entities.SpiderWeapon.RadiusAt(0f)))
            return "the meter didn't buy any width";

        // A shot under the floor is a clean refusal rather than a wasted round.
        var fizzle = new Entities.SpiderWeapon();
        fizzle.Hold(Entities.SpiderWeapon.MinCharge * 0.5f / Entities.SpiderWeapon.ChargeRate);
        if (fizzle.Release(Vector3.Zero, new Vector3(0f, 0f, 1f), out _))
            return "a charge under the floor still fired";
        return null;
    }

    private static int Check(string name, Func<string?> test)
    {
        string? err = test();
        if (err == null)
        {
            Console.WriteLine($"  PASS  {name}");
            return 0;
        }
        Console.WriteLine($"  FAIL  {name}: {err}");
        return 1;
    }

    // --- The wire ----------------------------------------------------------------
    // Nothing here touches the world yet. These check the two pieces a second player
    // needs before there can be one: that a tick of intent survives being turned into
    // bytes, and that the rig the rest of the netcode will be tested against actually
    // behaves like a bad connection.

    private static string? InputFrameRoundTrips()
    {
        var sent = new InputFrame(
            Btn.Forward | Btn.Fire | Btn.SpiderPounce | Btn.Jump,
            Btn.SpiderPounce | Btn.Jump,
            new Vector2(-13.5f, 240.25f))
            .WithAim(2.31f, -0.42f);

        Span<byte> wire = stackalloc byte[InputFrame.Size];
        sent.Write(wire);
        InputFrame got = InputFrame.Read(wire);

        if (got.Down != sent.Down) return "the held keys changed on the way through";
        if (got.Pressed != sent.Pressed) return "the edges changed on the way through";

        // What crosses is the ANGLE, not the mouse movement that produced it. A delta cannot
        // survive a lossy channel — the host integrates it, so a dropped packet is a piece of
        // the player's turn the host never gets back and can never be told about. An absolute
        // angle is self-correcting, and it is the thing the host is entitled to take at its
        // word, so it is the thing the wire has to carry faithfully.
        if (!got.HasAim) return "the frame arrived with nothing to say about where it points";
        if (MathF.Abs(got.Aim.X - 2.31f) > 1e-3f || MathF.Abs(got.Aim.Y - -0.42f) > 1e-3f)
            return $"the aim drifted: sent <2.31, -0.42>, got {got.Aim}";

        // And the delta is deliberately NOT carried. Asserted rather than merely unasserted:
        // a frame arriving with a live look delta would mean the host was turning the craft a
        // second time, on top of the angle it was just handed.
        if (got.LookX != 0 || got.LookY != 0)
            return "the mouse delta crossed the wire, so the host will turn the craft twice";

        // The named reads have to survive too — the world asks for those, not for bits.
        if (!got.Forward) return "a frame that was driving forward arrived stopped";
        if (!got.SpiderPouncePressed) return "the pounce edge didn't survive";
        if (got.RocketPressed) return "a rocket nobody fired arrived down the wire";

        // A locally-sampled frame must NOT claim to carry an aim: the machine that sampled it
        // turns its own craft with the delta, and a false claim here would have the host
        // stamping an angle of zero onto its own seat sixty times a second.
        if (new InputFrame(Btn.Forward, Btn.None, Vector2.Zero).HasAim)
            return "a frame nobody stamped claims to know where it points";
        return null;
    }

    private static string? EdgesAreSpentOnce()
    {
        // The bug this exists to prevent: the loop steps the sim off an accumulator, so a
        // slow display runs two fixed steps in one render frame. Polling raylib inside the
        // step would report the same click in both and the tank would fire twice for one
        // press. The sampler hands the edge to the first step and holds it back from the
        // rest, while held keys keep flowing so a craft under a held W keeps driving.
        var frame = new InputFrame(Btn.Forward | Btn.Fire, Btn.Fire, Vector2.One);

        InputFrame second = frame.Repeat();
        if (second.Hit(Btn.Fire)) return "the second step in one render frame fired again";
        if (!second[Btn.Forward]) return "a held throttle was dropped by the second step";
        if (second.LookDelta != Vector2.Zero)
            return "the frame's mouse movement was applied more than once";
        return null;
    }

    private static string? LoopbackCarriesTheTraffic()
    {
        // Six steps each way, no jitter, a quarter of everything unreliable lost. Heavy
        // loss on purpose: it makes the reliable guarantee below mean something.
        var net = new LoopbackNet(2, new LinkQuality(6, 0, 25f), seed: 7);
        INetTransport host = net[0], client = net[1];

        // Nothing arrives before its time.
        host.Send(1, "first"u8, reliable: true);
        for (int i = 0; i < 6; i++)
        {
            net.Advance();
            client.Pump();
            if (i < 5 && client.TryReceive(out _, out _))
                return $"a payload landed {5 - i} steps early";
        }
        if (!client.TryReceive(out int from, out byte[] got))
            return "a reliable payload never arrived";
        if (from != 0) return "the payload arrived from the wrong peer";
        if (!got.AsSpan().SequenceEqual("first"u8)) return "the payload arrived corrupted";

        // A hundred reliable sends: every one lands, and in the order they were sent.
        for (int i = 0; i < 100; i++)
            host.Send(1, BitConverter.GetBytes(i), reliable: true);

        var seen = new List<int>();
        for (int step = 0; step < 400; step++)
        {
            net.Advance();
            client.Pump();
            while (client.TryReceive(out _, out byte[] p)) seen.Add(BitConverter.ToInt32(p));
        }
        if (seen.Count != 100) return $"the reliable channel lost {100 - seen.Count} of 100";
        for (int i = 0; i < 100; i++)
            if (seen[i] != i) return $"the reliable channel delivered {seen[i]} where {i} belonged";

        // And unreliable traffic really is allowed to go missing, or the loss setting is
        // decorative and every test built on this rig is testing a perfect wire.
        int before = net.Dropped;
        for (int i = 0; i < 200; i++) host.Send(1, "x"u8, reliable: false);
        if (net.Dropped == before) return "a lossy wire dropped nothing at all";
        return null;
    }

    // --- Seats -------------------------------------------------------------------

    private static string? SoloIsUnchanged()
    {
        // The whole roster refactor is only safe if the game it started as still exists.
        // One seat, three lives, and World.Player finding the same craft it always did.
        var world = new World.World();
        if (world.Players.Count != 1) return $"a solo world seated {world.Players.Count} craft";
        if (world.LocalIndex != 0) return "the solo player wasn't in seat zero";
        if (!ReferenceEquals(world.Player, world.Players[0]))
            return "World.Player stopped pointing at the local craft";
        if (world.Player.Lives != 3)
            return $"the solo craft opened on {world.Player.Lives} lives, not the historical 3";
        if (world.Player.RevivesLeft != 2)
            return $"three lives should read as two revives, not {world.Player.RevivesLeft}";

        // A solo match is a one-seat match, so it is full the moment it exists and nobody
        // can be dropped into a game that was never opened to anyone.
        if (!world.Full) return "a one-seat solo world had room for a second player";
        if (world.AddPlayer() != null) return "someone joined a single-player run";
        return null;
    }

    private static string? SeatsFillToTheCap()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 20, Revives = 4 });

        // Seat zero exists from construction, so nineteen more fill it.
        for (int i = 1; i < 20; i++)
            if (world.AddPlayer() == null) return $"the host was refused seat {i} of twenty";

        if (world.Players.Count != 20) return $"twenty seats held {world.Players.Count} craft";
        if (!world.Full) return "a full match didn't say so";
        if (world.AddPlayer() != null) return "a twenty-first player got in";

        // Everyone gets the host's revive count, and nobody opens inside anybody else.
        foreach (var p in world.Players)
            if (p.RevivesLeft != 4) return $"a seat opened on {p.RevivesLeft} revives, not 4";

        for (int i = 1; i < world.Players.Count; i++)
            for (int j = i + 1; j < world.Players.Count; j++)
                if (Torus.Distance(world.Players[i].Position, world.Players[j].Position) < 1f)
                    return $"seats {i} and {j} opened on top of each other";

        // And the cap is not a suggestion: a host asking for a hundred gets twenty.
        var greedy = new World.World(null, new MatchSettings { MaxPlayers = 100 });
        if (greedy.Match.MaxPlayers != MatchSettings.MaxSeats)
            return $"a match capped at {greedy.Match.MaxPlayers} seats, above the twenty limit";
        return null;
    }

    /// <summary>
    /// Enough damage to take one whole life off a craft, whatever it is built from: every
    /// shield charge and then all of the hull behind them. Shields are no longer what kills
    /// you, so a test that wants a craft dead has to say so — <c>TakeDamage(MaxShield)</c>
    /// now leaves a player standing at 0/5 with a full hull, which is exactly right and
    /// exactly not what these tests are asking for.
    /// </summary>
    private static void SpendALife(PlayerTank p) => p.TakeDamage(p.MaxShield + p.MaxHealth);

    private static string? RevivesRunOutIntoSpectating()
    {
        // One revive: two lives. Dying once brings them back whole; dying twice puts them out
        // of the match without taking the match down with them.
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 1 });
        PlayerTank p = world.Player;
        if (p.RevivesLeft != 1) return $"one revive read as {p.RevivesLeft}";

        // Emptying the shields alone is not a death any more — the craft stands there with
        // nothing in front of its hull, which is the whole point of the two layers.
        p.TakeDamage(p.MaxShield);
        if (p.RevivesLeft != 1) return "a spent shield cost a life";
        if (p.ChargesLeft != 0) return $"{p.ChargesLeft} charges survived a shield's worth of damage";
        if (MathF.Abs(p.Health - p.MaxHealth) > 0.001f) return "damage reached the hull through a live shield";

        SpendALife(p);
        if (p.Spectating) return "a player with a revive left was sent to spectate";
        if (p.Shield < p.MaxShield - 0.001f) return "a revive didn't restore the shield";
        if (p.Health < p.MaxHealth - 0.001f) return "a revive didn't mend the hull";
        if (p.RevivesLeft != 0) return "the revive wasn't spent";

        SpendALife(p);
        if (!p.Spectating) return "a player out of revives is still playing";
        if (p.Alive) return "a spent player is somehow still alive";

        // The point of spectating: the rest of the field is untouched.
        var mate = world.AddPlayer();
        if (mate is null || mate.Spectating)
            return "one player running out took another down with them";

        // Zero revives is a legal choice and means exactly one life.
        var brutal = new World.World(null, new MatchSettings { Revives = 0 });
        if (brutal.Player.Lives != 1) return "a zero-revive match didn't give exactly one life";
        SpendALife(brutal.Player);
        if (!brutal.Player.Spectating) return "a zero-revive player survived their first death";
        return null;
    }

    private static string? FriendlyFireIsTheHostsCall()
    {
        var off = new World.World(null, new MatchSettings { MaxPlayers = 4, FriendlyFire = false });
        PlayerTank a = off.Player;
        PlayerTank b = off.AddPlayer()!;
        if (off.CanHarm(a, b)) return "friendly fire was off and a player could still be hit";

        var on = new World.World(null, new MatchSettings { MaxPlayers = 4, FriendlyFire = true });
        PlayerTank c = on.Player;
        PlayerTank d = on.AddPlayer()!;
        if (!on.CanHarm(c, d)) return "friendly fire was on and rounds passed straight through";

        // Never yourself, whatever the toggle says — a player's own splash has never hurt
        // them and turning the option on must not quietly change that.
        if (on.CanHarm(c, c)) return "a player's own round hurt them once friendly fire was on";
        if (off.CanHarm(a, a)) return "a player's own round hurt them";
        return null;
    }

    private static string? EverySeatDrivesItself()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        PlayerTank a = world.Player;
        PlayerTank b = world.AddPlayer()!;
        PlayerTank c = world.AddPlayer()!;

        Vector2 a0 = a.Position, b0 = b.Position, c0 = c.Position;

        // Seat 0 drives forward, seat 1 sits on its hands, seat 2 drives forward. Each
        // craft has to answer its own slot and nobody else's — the bug this guards is a
        // step that drives every player from the local player's keys.
        world.SetInput(1, InputFrame.Empty);
        world.SetInput(2, new InputFrame(Btn.Forward, Btn.None, Vector2.Zero));

        for (int i = 0; i < 90; i++)
            world.StepForTest((float)Config.FixedDt,
                new InputFrame(Btn.Forward, Btn.None, Vector2.Zero));

        float movedA = Torus.Distance(a.Position, a0);
        float movedB = Torus.Distance(b.Position, b0);
        float movedC = Torus.Distance(c.Position, c0);

        if (movedA < 1f) return "the local seat held forward and went nowhere";
        if (movedC < 1f) return "a remote seat held forward and went nowhere";
        if (movedB > 0.5f) return $"a seat with no input drifted {movedB:0.00} anyway";

        // The two that were driving are separate craft on separate bearings, so they must
        // not have ended up in the same place.
        if (Torus.Distance(a.Position, c.Position) < 1f)
            return "two seats driving their own keys converged on one spot";
        return null;
    }

    private static string? HuntersChaseTheNearest()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();

        PlayerTank near = world.AddPlayer()!;
        // Park the two craft as far apart as this world allows. The torus is 400 across, so
        // anything beyond ±Half is the same point coming back the other way — ±100 puts them
        // a genuine 200 apart, which is the furthest two things on it can ever be.
        world.Player.Position = Torus.Wrap(new Vector2(-100f, 0f));
        near.Position = Torus.Wrap(new Vector2(100f, 0f));

        var hunter = new EnemyTank(Torus.Wrap(new Vector2(160f, 0f)), elite: false);
        world.Enemies.Add(hunter);

        float toFar0 = Torus.Distance(hunter.Position, world.Player.Position);
        float toNear0 = Torus.Distance(hunter.Position, near.Position);
        if (toNear0 > toFar0) return "the test set itself up wrong: the hunter began nearer the far craft";

        // It picks its quarry every tick, so ask directly as well as watching where it goes.
        if (!ReferenceEquals(world.NearestPlayer(hunter.Position), near))
            return "the hunter chose the far craft over the one beside it";

        for (int i = 0; i < 180; i++) StepWithoutInput(world);

        float toNear = Torus.Distance(hunter.Position, near.Position);
        float toFar = Torus.Distance(hunter.Position, world.Player.Position);
        if (toNear > toFar)
            return $"the hunter walked past the craft beside it ({toNear:0}) to reach the far one ({toFar:0})";

        // Not asserting it closed the gap: a hunter holds a firing standoff and backing off
        // to it is correct behaviour. What matters is that it stayed committed to the near
        // craft rather than setting off across the world for the other one.
        if (toFar < toFar0 - 20f)
            return "the hunter set off for the far craft instead of engaging the near one";
        return null;
    }

    private static string? SpectatorsAreNotHunted()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 0 })
        { DynamicSpawning = false };
        world.Enemies.Clear();

        PlayerTank spent = world.AddPlayer()!;
        world.Player.Position = Torus.Wrap(new Vector2(-120f, 0f));
        spent.Position = Torus.Wrap(new Vector2(120f, 0f));

        // Spend the near player's single life. Their craft is still somewhere — the camera
        // has to be — but nothing on the field should be interested in it any more.
        SpendALife(spent);
        if (!spent.Spectating) return "the test failed to put the near player out";

        var hunter = new EnemyTank(Torus.Wrap(new Vector2(130f, 0f)), elite: false);
        world.Enemies.Add(hunter);

        if (!ReferenceEquals(world.NearestPlayer(hunter.Position), world.Player))
            return "a hunter picked a spectator as its quarry";

        for (int i = 0; i < 180; i++) StepWithoutInput(world);

        // It should have set off for the only player still playing, who is far away.
        if (Torus.Distance(hunter.Position, spent.Position) < 5f)
            return "the hunter camped on a player who was already out";
        return null;
    }

    private static string? RoundsCarryTheirOwner()
    {
        var round = new Entities.Projectile();

        round.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: Entities.Projectile.NoOwner);
        if (round.FromPlayer) return "a round the field fired claimed to be a player's";

        round.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: 3);
        if (!round.FromPlayer) return "seat three's round didn't count as a player's";
        if (round.Owner != 3) return $"seat three's round was stamped {round.Owner}";

        // The turned soldier: one of ours, belonging to no seat. It has to read as a player
        // round (so it bites hunters) while never being attributable to anybody.
        round.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: Entities.Projectile.AllyOwner);
        if (!round.FromPlayer) return "an ally's round read as an enemy's";
        if (round.Owner >= 0) return "an ally's round was pinned on a seat";

        // Pooled slots are reused, so the stamp must be overwritten rather than sticking.
        round.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: Entities.Projectile.NoOwner);
        if (round.FromPlayer) return "a reused slot kept the last shot's owner";
        return null;
    }

    /// <summary>Parks two craft nose to nose and has seat 0 empty the gun into seat 1.
    /// Returns how much shield the second one lost.</summary>
    private static float ShootTeamMate(bool friendlyFire)
    {
        var world = new World.World(null,
            new MatchSettings { MaxPlayers = 4, FriendlyFire = friendlyFire })
        { DynamicSpawning = false };
        world.Enemies.Clear();

        PlayerTank shooter = world.Player;
        PlayerTank mate = world.AddPlayer()!;

        shooter.Position = Torus.Wrap(new Vector2(0f, 0f));
        mate.Position = Torus.Wrap(new Vector2(0f, 12f));
        shooter.Heading = 0f;              // +Y, straight down the line at the mate
        mate.Heading = MathF.PI;

        float before = mate.Shield;
        for (int i = 0; i < 60 * 4; i++)
        {
            world.FirePlayerShot();
            StepWithoutInput(world);
        }
        return before - mate.Shield;
    }

    private static string? FriendlyFireOffSparesTheTeam()
    {
        float lost = ShootTeamMate(friendlyFire: false);
        return lost > 0.001f
            ? $"four seconds of point-blank fire cost a team-mate {lost:0.0} shield with friendly fire off"
            : null;
    }

    private static string? FriendlyFireOnHurtsTheTeam()
    {
        float lost = ShootTeamMate(friendlyFire: true);
        return lost <= 0.001f
            ? "friendly fire was on and four seconds of point-blank fire did nothing"
            : null;
    }

    /// <summary>
    /// The five destinations must actually be five places. A chart that is only a colour swap
    /// would pass every other test in this file, so this one asserts on the numbers the sim
    /// reads: no two worlds may agree on all of hostiles, gravity and view distance.
    /// </summary>
    private static string? TheFiveWorldsAreDifferentPlaces()
    {
        if (Planet.All.Count != 5) return $"the chart has {Planet.All.Count} worlds, not five";

        for (int i = 0; i < Planet.All.Count; i++)
        {
            Planet a = Planet.All[i];
            if ((int)a.Id != i) return $"{a.Name} is not at its own index — the wire sends the index";
            if (a.Murk is < 0f or > 1f) return $"{a.Name}'s murk is outside 0..1";
            if (a.Gravity <= 0f) return $"{a.Name} has no gravity at all";

            for (int j = i + 1; j < Planet.All.Count; j++)
            {
                Planet b = Planet.All[j];
                if (a.Name == b.Name) return $"two worlds are both called {a.Name}";
                if (Same(a.Hostiles, b.Hostiles) && Same(a.Gravity, b.Gravity) && Same(a.Murk, b.Murk))
                    return $"{a.Name} and {b.Name} are the same place with different paint";
            }
        }

        // The draw distance is the same everywhere and at every hour — a world says it is hard
        // to see across by going dark out there, never by pulling its horizon in. Checked at
        // both ends of SOLUNE's day, which is the only place a per-hour distance could hide.
        var solune = Planet.Get(PlanetId.Solune);
        foreach (float hour in new[] { 0f, 0.25f, 0.5f, 0.75f })
        {
            SkyLook look = solune.Look(hour);
            if (look.Murk is < 0f or > 1f) return $"SOLUNE's murk left 0..1 at hour {hour}";
        }
        if (Rendering.Atmosphere.FogStart != Config.FogStart
            || Rendering.Atmosphere.FogEnd != Config.FogEnd)
            return "the draw distance is not the fixed one";

        // Exactly one world turns. Two would need two clocks on the wire; none would make the
        // whole day/night system dead code.
        int cycling = Planet.All.Count(p => p.HasCycle);
        if (cycling != 1) return $"{cycling} worlds have a day/night cycle — there should be one";
        if (!Planet.Get(PlanetId.Solune).HasCycle) return "SOLUNE is not the one that turns";
        return null;

        static bool Same(float x, float y) => MathF.Abs(x - y) < 0.001f;
    }

    /// <summary>
    /// A world's conditions have to reach the things that read them: the craft's fall, and how
    /// hard the director pushes. Checked against ABYSSE and KIRENE, the two extremes.
    /// </summary>
    private static string? APlanetsConditionsReachTheSim()
    {
        var heavy = new World.World(null, new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Abysse });
        var light = new World.World(null, new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Kirene });

        if (heavy.Player.GravityScale <= light.Player.GravityScale)
            return "ABYSSE does not pull harder than KIRENE";
        if (Math.Abs(heavy.Player.GravityScale - Planet.Get(PlanetId.Abysse).Gravity) > 0.001f)
            return "the craft did not take the world's gravity";

        // Two worlds standing at once must not share their physics — the whole reason gravity
        // is an instance field and not a global.
        if (Math.Abs(light.Player.GravityScale - Planet.Get(PlanetId.Kirene).Gravity) > 0.001f)
            return "one world's gravity leaked into the other's";

        // A host's world is built at the HOST pillar, before anyone has been to the chart — so
        // the rules that arrive at LAUNCH are the first time it hears which world it is on, and
        // the craft already standing in it have to be re-weighted. Without this a host who
        // chose ABYSSE would launch everyone onto it and still jump like they were on SOLUNE.
        var early = new World.World(null, new MatchSettings { MaxPlayers = 4 });
        early.EnsureSeat(2);
        early.Match = new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Abysse };
        foreach (var craft in early.Players)
            if (Math.Abs(craft.GravityScale - Planet.Get(PlanetId.Abysse).Gravity) > 0.001f)
                return "a craft kept the old world's gravity after the rules named a new one";

        // The default solo run lands on SOLUNE and is otherwise the game as it always was: it
        // opens with a hunter and salvage, and the director is running.
        var solo = new World.World();
        if (solo.Match.Destination != PlanetId.Solune) return "a solo world did not default to SOLUNE";
        if (solo.Match.Mode != GameMode.Sandbox) return "a solo world did not default to SANDBOX";
        if (solo.Enemies.Count == 0) return "a solo world opened with no hunter";
        if (solo.Pickups.Count == 0) return "a solo world opened with no salvage";
        if (!solo.DynamicSpawning) return "a solo world has the spawn director off";
        return null;
    }

    /// <summary>
    /// SOLUNE's night has to be a thing the sim can feel, not just a thing the sky does: at
    /// midnight the hunters close in and the fog shuts down. Driven off the test hatch rather
    /// than by waiting six real minutes for the clock to come round.
    /// </summary>
    private static string? NightOnSoluneClosesIn()
    {
        var w = new World.World(null, new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Solune });
        w.Enemies.Add(new Entities.EnemyTank(new Vector2(0f, 60f), elite: false));
        var hunter = w.Enemies[^1];

        w.SetDayPhaseForTest(0.25f);   // noon
        float dayRange = hunter.PreferredRange;
        float dayMurk = w.Sky.Murk;
        Raylib_cs.Color dayHaze = w.Sky.Fog;
        if (w.Sky.Night > 0.01f) return "noon on SOLUNE registered as night";

        w.SetDayPhaseForTest(0.75f);   // midnight
        if (w.Sky.Night < 0.99f) return "midnight on SOLUNE did not register as night";
        if (hunter.PreferredRange >= dayRange) return "the hunters held the same range after dark";

        // The far field goes darker, and it does it WITHOUT the horizon moving: the whole point
        // is that the same geometry is still drawn, you just cannot make it out any more.
        if (w.Sky.Murk <= dayMurk) return "the far field did not go murkier after dark";
        if (Brightness(w.Sky.Fog) >= Brightness(dayHaze))
            return "the haze did not darken after dark";
        if (Rendering.Atmosphere.FogEnd != Config.FogEnd)
            return "nightfall moved the draw distance";

        // A world with no cycle never moves, however long it is stepped, and never registers
        // as night.
        var still = new World.World(null, new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Verene });
        float opened = still.DayPhase;
        for (int i = 0; i < 60 * 30; i++) still.StepForTest((float)Config.FixedDt);
        if (still.DayPhase != opened) return "a world with no cycle turned anyway";
        if (still.Sky.Night != 0f) return "a world with no cycle went dark";

        // And a fresh SOLUNE opens in daylight rather than at dawn — landing must not drop
        // people straight into the half-dark with the fog already shut.
        var fresh = new World.World(null, new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Solune });
        if (fresh.Sky.Night > 0.01f) return "a fresh SOLUNE opened in twilight";
        return null;

        static float Brightness(Raylib_cs.Color c) => c.R + c.G + c.B;
    }

    /// <summary>The lobby's new ENEMIES switch is a fifth rules byte; it has to cross the wire
    /// beside the rest and default to on, or a host who turned the fight off would launch a
    /// match every client still thinks is full of hunters.</summary>
    private static string? RulesSurviveTheWire()
    {
        var m = new MatchSettings
        {
            MaxPlayers = 12, FriendlyFire = true, Revives = 7,
            Destination = PlanetId.Thalos, Mode = GameMode.Descent, SpawnEnemies = false,
        };
        Span<byte> buf = stackalloc byte[MatchSettings.Size];
        m.Write(buf);
        MatchSettings r = MatchSettings.Read(buf);

        if (r.SpawnEnemies) return "the enemies-off toggle did not survive the wire";
        if (r.Destination != PlanetId.Thalos) return "the destination did not survive the wire";
        if (r.Mode != GameMode.Descent) return "the mode did not survive the wire";
        if (r.MaxPlayers != 12 || !r.FriendlyFire || r.Revives != 7)
            return "the rest of the rules did not survive alongside the destination";

        // A byte naming a world this build does not have must land on SOLUNE rather than on
        // an index into nothing — a malformed welcome must not be able to crash a joiner.
        Span<byte> junk = stackalloc byte[MatchSettings.Size];
        m.Write(junk);
        junk[3] = 200;
        junk[5] = 200;
        MatchSettings safe = MatchSettings.Read(junk);
        if (safe.Destination != PlanetId.Solune) return "a nonsense destination was not clamped";
        if (safe.Mode != GameMode.Sandbox) return "a nonsense mode was not clamped";

        // The default is a match with enemies — an omitted/older setting must never read as off.
        Span<byte> def = stackalloc byte[MatchSettings.Size];
        new MatchSettings().Write(def);
        if (!MatchSettings.Read(def).SpawnEnemies) return "the default match lost its enemies";
        return null;
    }

    /// <summary>
    /// A client never runs the spawn director, but it does draw the sky — so the hour has to
    /// reach it or two people standing on SOLUNE together would be in different halves of the
    /// day. It rides the field packet; this drives the same two-world rig the rest of the
    /// netcode is built against and asks whether the client's clock followed the host's.
    /// </summary>
    private static string? TheHoursCrossesTheWire()
    {
        var net = new LoopbackNet(2, LinkQuality.Typical, seed: 4242);
        var rules = new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Solune };

        var hostWorld = new World.World(null, rules) { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, rules) { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        // Put the host's clock at dusk and the client's at dawn, then let them talk. Started
        // deliberately apart: two clocks that agreed to begin with would keep agreeing on
        // their own, and the test would pass without a byte crossing.
        hostWorld.SetDayPhaseForTest(0.5f);
        clientWorld.SetDayPhaseForTest(0f);

        for (int i = 0; i < 600; i++)   // ten seconds
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hostWorld.StepForTest((float)Config.FixedDt);
            clientWorld.StepForTest((float)Config.FixedDt);
        }

        if (client.LastAppliedTick == 0) return "the client never applied a single snapshot";

        float gap = MathF.Abs(hostWorld.DayPhase - clientWorld.DayPhase);
        if (gap > 0.5f) gap = 1f - gap;   // shortest way round the dial
        if (gap > 0.01f)
            return $"the client's clock is {gap:0.000} of a day off the host's after ten seconds";

        // And the clock actually ran — a pair of stopped clocks also agree.
        if (hostWorld.DayPhase == 0.5f) return "the host's day never advanced";
        return null;
    }

    /// <summary>
    /// The whole point of putting the destination to the room: three people vote, the world
    /// most of them wanted is the one the match is bound for, and every machine is told. Uses
    /// the real sessions and the real countdown — no shortcuts through the resolution, because
    /// the bug this guards against is a client resolving a tie for itself.
    /// </summary>
    private static string? TheRoomVoteSettles()
    {
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 8, Revives = 2 });

        var host = s.Host.Room!;
        if (host.Match.Destination != PlanetId.Solune) return "the room did not open on SOLUNE";

        host.OpenVoteForTest();
        s.Step(20);

        // Everyone must have been told a vote is running — including the two clients, whose
        // charts only know because the opening packet reached them.
        foreach (Machine m in s.All)
            if (m.Room is { } r && !r.Chart.VoteOpen)
                return "a machine in the room was never told the vote had opened";

        // Two for THALOS, one for ABYSSE. THALOS must win outright — no tie, so nothing here
        // depends on the coin flip.
        s.All[1].Room!.Chart.PointAt(PlanetId.Thalos);
        s.All[1].Room!.Chart.CastLocal(s.All[1].Net.LocalSeat);
        s.All[2].Room!.Chart.PointAt(PlanetId.Thalos);
        s.All[2].Room!.Chart.CastLocal(s.All[2].Net.LocalSeat);
        host.Chart.PointAt(PlanetId.Abysse);
        host.Chart.CastLocal(host.LocalSeat);
        s.Step(30);

        if (host.Chart.Tally(PlanetId.Thalos) != 2)
            return $"the host counted {host.Chart.Tally(PlanetId.Thalos)} votes for THALOS, not 2";
        foreach (Machine m in s.All)
            if (m.Room is { } r && r.Chart.Tally(PlanetId.Thalos) != 2)
                return "a client's tally disagreed with the host's";

        // Run the clock out. Twenty seconds at sixty a second, plus a little to let the
        // result travel.
        s.Step((int)(UI.StarMap.VoteDuration * 60f) + 60);

        if (host.Chart.VoteOpen) return "the vote never closed";
        if (host.Match.Destination != PlanetId.Thalos)
            return $"the room voted THALOS and the host settled on {host.Match.World.Name}";
        foreach (Machine m in s.All)
        {
            if (m.Room is not { } r) continue;
            if (r.Match.Destination != PlanetId.Thalos)
                return $"a client was never told the room chose THALOS (it has {r.Match.World.Name})";
            if (r.Chart.VoteOpen) return "a client's countdown never stopped";
        }
        return null;
    }

    /// <summary>Enemies off is independent of the destination: the world keeps its city and its
    /// salvage, but seeds and spawns nothing hostile — no hunters, no bosses, no squads, ever.
    /// This is what the retired FLAT map used to be, without giving up the sky you chose.</summary>
    private static string? EnemiesOffEmptiesThePlanet()
    {
        var world = new World.World(null,
            new MatchSettings { MaxPlayers = 4, Destination = PlanetId.Solune, SpawnEnemies = false });

        if (world.Structures.Count == 0) return "enemies-off threw the city away";
        if (world.Pickups.Count == 0) return "enemies-off seeded no salvage — it is not a hostile";
        if (world.Enemies.Count != 0) return $"enemies-off opened with {world.Enemies.Count} hunters";
        if (world.Boss != null || world.Maw != null) return "enemies-off raised a boss";

        // Half a minute of stepping must conjure nothing hostile out of the director.
        for (int i = 0; i < 60 * 30; i++) world.StepForTest((float)Config.FixedDt);
        if (world.Enemies.Count != 0) return $"enemies-off spawned {world.Enemies.Count} hunters over 30s";
        if (world.Boss != null || world.Maw != null) return "enemies-off spawned a boss over 30s";
        if (world.Soldiers.Count != 0) return $"enemies-off spawned {world.Soldiers.Count} soldiers over 30s";
        return null;
    }

    /// <summary>The body a mote has seized, and how far it has rotted, ride the PLAYERS packet
    /// so every other machine draws the worn host rather than the naked mote — the whole of the
    /// virus being visible in play. A seat that is not the client's own is driven from the
    /// snapshot; here seat 2 (an elite-wearing virus) must arrive wearing the elite, at the same
    /// integrity the host had.</summary>
    private static string? VirusHostCrossesTheWire()
    {
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        host.AddPlayer(new Loadout { Class = PlayerClass.Tank });    // seat 1
        host.AddPlayer(new Loadout { Class = PlayerClass.Virus });   // seat 2

        PlayerTank v = host.Players[2];
        if (v.Virus is not { } worn) return "the virus seat has no rig to wear a host with";
        worn.Possess(v, Entities.VirusHost.Elite);
        // Let the husk rot a little so a non-trivial integrity has to cross, not just a flat 1.
        for (int i = 0; i < 40; i++) host.StepForTest((float)Config.FixedDt);
        if (!worn.Hosted) return "the elite fell off the host before the snapshot was taken";

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        client.AddPlayer();        // seat 1
        client.LocalIndex = 1;     // the virus at seat 2 is not ours, so it is driven from the wire

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, tick: 11u, buf);
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));

        if (client.Players.Count < 3 || client.Players[2].Virus is not { } cv)
            return "the worn virus seat did not arrive with a rig on the client";
        if (cv.HostKind != Entities.VirusHost.Elite)
            return $"the worn body crossed as {cv.HostKind}, not the elite it was";
        if (MathF.Abs(cv.Integrity - worn.Integrity) > 0.01f)
            return $"the host's rot crossed at {cv.Integrity:0.000}, not the {worn.Integrity:0.000} it was";
        return null;
    }

    /// <summary>
    /// The fix for the invulnerable-client bug. A client used to skip its own seat wholesale when
    /// a players packet landed (<c>seat == LocalIndex</c> → continue), so its own craft could
    /// never take shield damage, lose a life, or be seized on its own screen — every consequence
    /// the host computed was thrown away for the one craft the player was driving. Now the host's
    /// STATUS for the local seat is applied (shield, lives, ammo, capture); its TRANSFORM is left
    /// to the client's own prediction to reconcile toward, not snapped.
    /// </summary>
    private static string? OwnCraftObeysTheHost()
    {
        var host = new World.World(new Loadout { Class = PlayerClass.Tank })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        host.LocalIndex = 0;
        PlayerTank hp = host.Players[0];
        hp.Shield = 30f;      // the host has taken a beating
        hp.Lives = 1;
        hp.Ammo = 7;
        hp.Captured = true;   // ...and been grabbed by the crab
        hp.Position = Torus.Wrap(new Vector2(10f, 0f));

        var client = new World.World(new Loadout { Class = PlayerClass.Tank })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        PlayerTank cp = client.Players[0];
        cp.Shield = cp.MaxShield;   // the client thinks it is whole and free
        cp.Lives = 3;
        cp.Ammo = cp.MaxAmmo;
        cp.Captured = false;
        cp.Position = Torus.Wrap(new Vector2(0f, 0f));   // a predicted spot away from the host's

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, tick: 1u, buf);
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));

        // Status is the host's now — the whole point of the fix.
        if (MathF.Abs(cp.Shield - 30f) > 0.01f)
            return $"the client's own shield stayed {cp.Shield:0.0}, not the host's 30";
        if (cp.Lives != 1) return $"the client's own lives stayed {cp.Lives}, not the host's 1";
        if (cp.Ammo != 7) return $"the client's own ammo stayed {cp.Ammo}, not the host's 7";
        if (!cp.Captured) return "the host grabbed the client but its own craft never knew";

        // Transform is reconciled, not snapped: applying the packet must not teleport the craft to
        // the host's position — that is the prediction's to ease toward, not the packet's to impose,
        // or steering would judder in the player's hands.
        if (Torus.Distance(cp.Position, Torus.Wrap(new Vector2(0f, 0f))) > 0.01f)
            return "applying a snapshot snapped the client's own craft instead of reconciling it";
        return null;
    }

    /// <summary>
    /// Interpolation, the fix for 20 Hz stutter. A remote craft used to be snapped to each
    /// snapshot — twenty visible steps a second. Now the host's latest position is a target the
    /// client eases toward every frame, so between two snapshots the craft is found part-way,
    /// gliding, and applying a snapshot never teleports it.
    /// </summary>
    private static string? RemoteCraftInterpolates()
    {
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        PlayerTank watched = host.AddPlayer()!;   // seat 1 — the craft the client will watch

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        client.Structures.Clear();   // nothing to shove the puppet around while we watch it glide

        var buf = new byte[Snapshot.MaxSize];

        // First snapshot: the craft is at A. First sight snaps, so the client opens it there.
        Vector2 a = Torus.Wrap(new Vector2(20f, 0f));
        watched.Position = a;
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, Snapshot.WritePlayers(host, 1u, buf)));
        if (client.Players.Count < 2) return "the client never learned about the watched craft";
        if (Torus.Distance(client.Players[1].Position, a) > 0.1f)
            return "a craft first seen did not open where the host had it";

        // Second snapshot: it has jumped to B. The packet must NOT teleport the client's copy —
        // that is only the target now, and the craft sits at A until a step eases it.
        Vector2 b = Torus.Wrap(new Vector2(40f, 0f));
        watched.Position = b;
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, Snapshot.WritePlayers(host, 2u, buf)));
        if (Torus.Distance(client.Players[1].Position, a) > 0.1f)
            return "applying a snapshot snapped the remote craft instead of easing it";

        // One step eases it part-way — strictly between where it was and where it is going.
        client.StepForTest((float)Config.FixedDt);
        if (Torus.Distance(client.Players[1].Position, a) < 0.5f)
            return "a step did not move the remote craft toward the host's new position";
        if (Torus.Distance(client.Players[1].Position, b) < 0.5f)
            return "a single step snapped the remote craft all the way, instead of easing it";

        // Many steps settle it onto B.
        for (int i = 0; i < 120; i++) client.StepForTest((float)Config.FixedDt);
        if (Torus.Distance(client.Players[1].Position, b) > 0.5f)
            return "the remote craft never settled onto the host's position";
        return null;
    }

    /// <summary>
    /// Effect replication. A client runs no combat, so a kill throws debris on the host's screen
    /// and, before this, nothing on anyone else's — the field's deaths were silent puffs of
    /// nothing to onlookers. Now the host records each burst and sends it, and the client replays
    /// it. Headless there is no window, so this asserts the burst crossed and spawned pieces.
    /// </summary>
    private static string? EffectCrossesTheWire()
    {
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 77);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        if (client.LocalSeat != 1) return $"the joiner never seated (at {client.LocalSeat})";

        // The host blows a hunter apart where both seats opened. A client throws no debris of its
        // own — it runs no combat — so the only way it ever sees this burst is over the wire.
        Vector2 at = hostWorld.Players[1].Position;
        hostWorld.Debris.Burst(new Vector3(at.X, 1f, at.Y), Palette.EliteFill, elite: true);

        for (int i = 0; i < 12; i++) { net.Advance(); host.Pump(InputFrame.Empty); client.Pump(InputFrame.Empty); }

        if (clientWorld.RemoteEffectsPlayed < 1)
            return "the host threw debris and the client never saw it";
        bool anyShard = false;
        foreach (var s in clientWorld.Debris.Shards) if (s.Active) { anyShard = true; break; }
        if (!anyShard) return "the effect crossed the wire but spawned no debris on the client";
        return null;
    }

    /// <summary>
    /// The onlooker's half of the crab seizure. The held player's own transform already crosses
    /// the wire (players packet), so watchers see them dragged up and thrown — but the boss's
    /// arms were the host's alone, so to everyone else the boss stood idle beside a craft floating
    /// in the air. Now the grab and strike arms and the seizure blaze ride the bosses packet and
    /// pose the puppet, and fall back to the plain showcase pose the instant it lets go.
    /// </summary>
    private static string? SeizureArmCrossesTheWire()
    {
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        host.SpawnCrabAhead();
        // The host has a player in its claw mid-cinematic: the front-right limb grips, the
        // front-left is wound part-way into the club, and the core is blazing.
        host.Boss!.DriveSeizure(held: true, grabArm: 0.8f, strikeArm: 0.3f, glow: 0.5f);

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;

        var buf = new byte[64];
        Snapshot.ApplyBosses(client, buf.AsSpan(0, Snapshot.WriteBosses(host, forSeat: 0, tick: 7u, buf)));

        if (client.Boss is not { IsPuppet: true } p) return "the seizing boss never reached the client";
        CrabPose pose = p.Pose;
        if (MathF.Abs(pose.GrabArm - 0.8f) > 0.02f)
            return $"the grab arm crossed as {pose.GrabArm:0.00}, not the host's 0.80";
        if (MathF.Abs(pose.StrikeArm - 0.3f) > 0.02f)
            return $"the strike arm crossed as {pose.StrikeArm:0.00}, not the host's 0.30";

        // Let go, and the puppet falls straight back to the plain showcase pose — arms down.
        host.Boss.DriveSeizure(held: false, grabArm: 0f, strikeArm: 0f, glow: 0f);
        Snapshot.ApplyBosses(client, buf.AsSpan(0, Snapshot.WriteBosses(host, forSeat: 0, tick: 8u, buf)));
        if (client.Boss!.Pose.GrabArm > 0.001f || client.Boss.Pose.StrikeArm > 0.001f)
            return "the boss let go but the puppet kept the grab arm out";
        return null;
    }

    /// <summary>Seats a client into a started host match over a loopback and hands both
    /// sessions back. Pumps the lobby until the handshake settles.</summary>
    private static (LoopbackNet net, Session host, Session client, World.World hw, World.World cw)
        SeatOne(int maxPlayers, string name)
    {
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 3);
        var hw = new World.World(null, new MatchSettings { MaxPlayers = maxPlayers })
        { DynamicSpawning = false };
        hw.Enemies.Clear();
        var cw = new World.World(new Loadout { Class = PlayerClass.Tank })
        { DynamicSpawning = false };

        var host = new Session(net[0], host: true) { LocalName = "HOST" };
        var client = new Session(net[1], host: false) { LocalName = name };
        host.HostMatch(hw);
        client.JoinMatch(cw);
        client.SendHello(PlayerClass.Tank);

        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        host.StartMatch();
        for (int i = 0; i < 20; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        return (net, host, client, hw, cw);
    }

    private static string? DropAndRejoinRestoresTheSeat()
    {
        var (net, host, client, hw, cw) = SeatOne(4, "ACE");
        if (client.LocalSeat != 1) return $"the client seated at {client.LocalSeat}, not 1";

        // Play a little: the held craft has a distinct state to restore. The host owns it.
        PlayerTank seat1 = hw.Players[1];
        seat1.Position = Torus.Wrap(new Vector2(50f, -25f));
        seat1.Lives = 2;
        seat1.Ammo = 13;

        // The player drops. The host must hold the seat, not free it, and tell the room.
        net.DropPeer(0, 1);
        for (int i = 0; i < 5; i++) { net.Advance(); host.PumpLobby(); }
        if (!hw.Players[1].Away) return "a dropped player's seat was not marked away";
        if (hw.Players.Count != 2) return "the dropped player's seat was removed, not held";
        if (!FeedHas(host.Notices, "LEFT")) return "no LEFT notice was posted";
        if (!FeedHas(host.Notices, "ACE")) return "the LEFT notice did not name the player";

        // While away, the craft is frozen: stepping the host must not move or age it.
        Vector2 held = hw.Players[1].Position;
        for (int i = 0; i < 120; i++) hw.StepForTest((float)Config.FixedDt);
        if (Torus.Distance(hw.Players[1].Position, held) > 0.01f)
            return "an away craft drifted while its player was gone";

        // They come back — same Steam account, so the same seat, with everything restored.
        net.Readmit(0, 1);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }

        if (hw.Players[1].Away) return "the seat was still away after a rejoin";
        if (client.LocalSeat != 1) return $"the rejoiner landed in seat {client.LocalSeat}, not their old 1";
        if (cw.Players[client.LocalSeat].Lives != 2)
            return $"lives came back as {cw.Players[client.LocalSeat].Lives}, not the held 2";
        if (cw.Players[client.LocalSeat].Ammo != 13)
            return $"ammo came back as {cw.Players[client.LocalSeat].Ammo}, not the held 13";
        if (Torus.Distance(cw.Players[client.LocalSeat].Position, held) > 0.5f)
            return "the rejoiner did not open where their craft was held";
        if (!FeedHas(host.Notices, "REJOINED")) return "no REJOINED notice was posted";
        return null;
    }

    private static string? FullMatchStillLetsYouBack()
    {
        // A two-seat match with both seats taken.
        var (net, host, client, hw, cw) = SeatOne(2, "BEE");
        if (!hw.Full) return "a two-of-two match did not call itself full";
        if (hw.AddPlayer() != null) return "a full match seated a third craft";

        // The one joiner drops. The match is still full (the seat is held), but the person who
        // holds it must be let back even so — a rejoin reuses the seat rather than needing a
        // free one.
        net.DropPeer(0, 1);
        for (int i = 0; i < 5; i++) { net.Advance(); host.PumpLobby(); }
        net.Readmit(0, 1);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }

        if (hw.Players[1].Away) return "a full match refused to let its own dropped player back";
        if (client.LocalSeat != 1) return "the rejoiner was not restored to their seat";
        return null;
    }

    /// <summary>
    /// A player who reconnects has to be able to DRIVE, not merely reappear.
    ///
    /// The host buffers each seat's input by tick number and throws away anything at or below
    /// the highest tick it has already taken from that seat — which is what makes the redundant
    /// copies on every packet free to ignore, and what stops a packet that overtook a newer one
    /// from winding a craft backwards. It also sets a trap. A machine that reconnects is a NEW
    /// session and its tick counter starts again at one, so a seat still holding the old
    /// high-water mark would reject every frame that player sent for the next several minutes.
    /// Their craft would sit in the world, correctly restored, with the controls apparently
    /// dead, and nothing anywhere would say why.
    ///
    /// The existing rejoin tests cannot catch it: they reuse the same session object, whose
    /// clock keeps counting. This one rebuilds the client the way a real reconnection does, and
    /// then leans on the throttle.
    /// </summary>
    private static string? ARejoinedPlayerCanStillDrive()
    {
        var (net, host, client, hw, cw) = SeatOne(4, "ACE");
        if (client.LocalSeat != 1) return $"the client seated at {client.LocalSeat}, not 1";

        // Long enough that the seat's input clock is well past anything a fresh session would
        // produce for a good while — which is exactly the state that used to be fatal.
        var drive = new InputFrame(Btn.Forward, Btn.None, Vector2.Zero);
        for (int i = 0; i < 400; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(drive);
            hw.Update((float)Config.FixedDt, InputFrame.Empty);
            cw.Update((float)Config.FixedDt, drive);
        }

        // Hands off the keys before the drop. This matters to the test rather than to the game:
        // the host's last-frame fallback carries HELD keys forward, so a seat that disconnects
        // mid-throttle would coast on that stale frame and a craft that moved afterwards would
        // prove nothing about whether the rejoiner's own input was getting through.
        for (int i = 0; i < 20; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hw.Update((float)Config.FixedDt, InputFrame.Empty);
            cw.Update((float)Config.FixedDt, InputFrame.Empty);
        }

        // They drop, and come back as a new machine: a new session on the same wire, with its
        // own clock starting from nothing.
        net.DropPeer(0, 1);
        for (int i = 0; i < 5; i++) { net.Advance(); host.Pump(InputFrame.Empty); }
        net.Readmit(0, 1);

        var cw2 = new World.World(new Loadout { Class = PlayerClass.Tank })
        { DynamicSpawning = false, Authoritative = false };
        cw2.Enemies.Clear();
        var rejoined = new Session(net[1], host: false) { LocalName = "ACE" };
        rejoined.JoinMatch(cw2);
        rejoined.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            rejoined.Pump(InputFrame.Empty);
            hw.Update((float)Config.FixedDt, InputFrame.Empty);
            cw2.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (rejoined.LocalSeat != 1) return "the rejoiner was not restored to their seat";
        if (hw.Players[1].Away) return "the host still has the rejoined player marked away";

        // And now the whole point: the throttle has to reach the host.
        Vector2 before = hw.Players[1].Position;
        for (int i = 0; i < 180; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            rejoined.Pump(drive);
            hw.Update((float)Config.FixedDt, InputFrame.Empty);
            cw2.Update((float)Config.FixedDt, drive);
        }

        float went = Torus.Distance(hw.Players[1].Position, before);
        if (went < 20f)
            return $"a rejoined player leaning on the throttle for three seconds moved {went:0.0} "
                 + "on the host — their input is being thrown away as stale";
        return null;
    }

    private static bool FeedHas(NoticeFeed feed, string needle)
    {
        foreach (var e in feed.Entries)
            if (e.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? FieldPacketIsKeepLast()
    {
        // The heart of the vanish-bug fix: players and field are separate packets, and a
        // client that misses a field packet must keep the field it already had rather than
        // clearing it. This proves the two are independent — applying only a players packet
        // leaves the enemies untouched, and the busy field never rides in the players packet
        // that always has to fit.
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        for (int i = 0; i < 20; i++)
            host.Enemies.Add(new EnemyTank(Torus.Wrap(new Vector2(i * 3f, 10f)), elite: false));
        host.AddPlayer();

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        client.AddPlayer();
        client.LocalIndex = 0;

        var buf = new byte[Snapshot.MaxSize];

        // First, a field packet lands, so the client learns the enemies.
        int nf = Snapshot.WriteField(host, forSeat: 0, tick: 1u, buf);
        Snapshot.ApplyField(client, buf.AsSpan(0, nf));
        int had = client.Enemies.Count;
        if (had == 0) return "the client never received the field at all";

        // Now several players packets arrive with no field packet between them — a run of
        // field loss. The enemies must still be standing.
        for (int t = 2; t < 8; t++)
        {
            int np = Snapshot.WritePlayers(host, (uint)t, buf);
            Snapshot.ApplyPlayers(client, buf.AsSpan(0, np));
        }
        if (client.Enemies.Count != had)
            return $"a run of lost field packets left {client.Enemies.Count} enemies, not {had}";

        // The players packet alone is small enough to always fit one datagram — the property
        // the whole split rests on. A full house must not exceed the players-packet ceiling.
        var full = new World.World(null, new MatchSettings { MaxPlayers = MatchSettings.MaxSeats })
        { DynamicSpawning = false };
        for (int i = 1; i < MatchSettings.MaxSeats; i++) full.AddPlayer();
        int fp = Snapshot.WritePlayers(full, 1u, buf);
        if (fp > 1100) return $"a full players packet is {fp} bytes, over a safe datagram";
        return null;
    }

    private static string? ChassisCrossesTheWire()
    {
        // A joiner who picked a fish has to arrive as a fish on every other screen — the whole
        // point of the class byte in the snapshot. The client's roster starts as placeholder
        // tanks, so this also exercises the rebuild that swaps one for the real chassis.
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        host.AddPlayer(new Loadout { Class = PlayerClass.Fish });
        host.AddPlayer(new Loadout { Class = PlayerClass.Soldier });

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        client.AddPlayer();   // seat 1, a placeholder tank
        client.AddPlayer();   // seat 2, a placeholder tank
        client.LocalIndex = 0;

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, tick: 5u, buf);
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));

        if (client.Players[1].Class != PlayerClass.Fish)
            return $"seat 1 picked a fish and arrived as {client.Players[1].Class}";
        if (client.Players[2].Class != PlayerClass.Soldier)
            return $"seat 2 picked a soldier and arrived as {client.Players[2].Class}";
        // The rebuilt craft actually has the rig, not just the label.
        if (client.Players[1].Fish is null) return "the fish arrived without a fish rig";
        if (client.Players[2].Soldier is null) return "the soldier arrived without a soldier rig";

        // Seat 0 is this client's own craft and must never be rebuilt out from under it.
        if (!ReferenceEquals(client.Player, client.Players[0]))
            return "the local craft was replaced by a snapshot";
        return null;
    }

    private static string? ClientGrowsForLaterSeats()
    {
        // A client seated at 1 only builds up to its own seat when it joins. When a third
        // player takes seat 2, every snapshot names them — and before the roster grew to fit,
        // the client created them never and they stayed invisible. Applying a snapshot that
        // names a higher seat than the client holds must grow the roster to cover it, then
        // rebuild it as the right chassis, alive enough to draw.
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        host.AddPlayer(new Loadout { Class = PlayerClass.Spider });  // seat 1
        host.AddPlayer(new Loadout { Class = PlayerClass.Virus });   // seat 2

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        client.AddPlayer();        // seat 1, this client's own — all it knew at Welcome
        client.LocalIndex = 1;

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, tick: 7u, buf);
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));

        if (client.Players.Count < 3)
            return $"client saw {client.Players.Count} seats, not the host's 3 — a later joiner is invisible";
        if (client.Players[2].Class != PlayerClass.Virus)
            return $"the later joiner arrived as {client.Players[2].Class}, not the virus they picked";
        if (!client.Players[2].Alive)
            return "the later joiner arrived not alive, so it would draw as nothing";
        return null;
    }

    private static string? SeatsOpenWithinSight()
    {
        // Two players in a fresh match have to open close enough, and facing the right way,
        // that each is in the other's view on the first frame — the thing that makes "did the
        // other player actually connect" answerable by looking rather than by driving around.
        var world = new World.World(null, new MatchSettings { MaxPlayers = 8 });
        PlayerTank a = world.Player;
        PlayerTank b = world.AddPlayer()!;

        float gap = Torus.Distance(a.Position, b.Position);
        if (gap > 30f) return $"two players opened {gap:0} apart, too far to see each other";
        if (gap < 4f) return $"two players opened {gap:0} apart, all but on top of each other";

        // The joiner faces back toward the host, so the host looking forward sees them. Their
        // forward should point roughly from b toward a.
        Vector2 bToA = Torus.Delta(b.Position, a.Position);
        float align = Vector2.Dot(Vector2.Normalize(bToA), b.Forward);
        if (align < 0.5f) return "the joiner opened facing away from the host";
        return null;
    }

    private static string? SnapshotRoundTrips()
    {
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        PlayerTank second = host.AddPlayer()!;
        second.Position = Torus.Wrap(new Vector2(37.5f, -84.25f));
        second.Heading = 1.25f;
        second.Shield = 42f;
        second.Lives = 2;
        second.Ammo = 17;

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        client.AddPlayer();
        client.LocalIndex = 0;

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, tick: 99u, buf);
        if (n > Snapshot.MaxSize) return $"a players packet wrote {n} bytes into a {Snapshot.MaxSize} buffer";

        uint tick = Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));
        if (tick != 99u) return $"the tick came back as {tick}, not 99";

        PlayerTank copy = client.Players[1];
        if (Torus.Distance(copy.Position, second.Position) > 0.05f)
            return $"the craft landed {Torus.Distance(copy.Position, second.Position):0.000} from where it was sent";
        if (MathF.Abs(copy.Shield - 42f) > 0.5f) return $"shield arrived as {copy.Shield}";
        if (copy.Lives != 2) return $"lives arrived as {copy.Lives}";
        if (copy.Ammo != 17) return $"ammo arrived as {copy.Ammo}";

        // A truncated packet must leave the world alone rather than throwing.
        Vector2 before = copy.Position;
        if (Snapshot.ApplyPlayers(client, buf.AsSpan(0, 6)) != 0u)
            return "a truncated packet was accepted";
        if (client.Players[1].Position != before)
            return "a truncated packet moved something before it gave up";
        return null;
    }

    private static string? ClientTracksTheHost()
    {
        // Two worlds, two sessions, one deliberately poor connection between them. This is
        // the rig the rest of the netcode gets built against: if a desync shows up it shows
        // up here, in one process, with both worlds sitting in the debugger.
        var net = new LoopbackNet(2, LinkQuality.Typical, seed: 4242);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();

        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };   // a real client: it eases remotes, not simulates them
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        var drive = new InputFrame(Btn.Forward, Btn.None, Vector2.Zero);
        Vector2 hostStart = hostWorld.Player.Position;

        for (int i = 0; i < 600; i++)     // ten seconds
        {
            net.Advance();
            host.Pump(drive);                      // the host drives forward
            client.Pump(InputFrame.Empty);         // the client sits still
            hostWorld.StepForTest((float)Config.FixedDt, drive);
            clientWorld.StepForTest((float)Config.FixedDt, InputFrame.Empty);
        }

        if (client.LocalSeat != 1) return $"the client was seated at {client.LocalSeat}, not 1";
        if (host.World!.Players.Count != 2) return "the host never seated the joining player";
        if (clientWorld.Players.Count < 2) return "the client never learned about the host's craft";
        if (client.LastAppliedTick == 0) return "the client never applied a single snapshot";

        // The host actually went somewhere, and the client's copy of it followed.
        float hostMoved = Torus.Distance(hostWorld.Player.Position, hostStart);
        if (hostMoved < 5f) return $"the host only moved {hostMoved:0.0} in ten seconds";

        // Seat 0 on the client is the host's craft — the one thing the client is told about
        // and never drives. A hundred milliseconds of lag is about two units at this speed,
        // so a couple of units of lag is expected and ten is a desync.
        float gap = Torus.Distance(clientWorld.Players[0].Position, hostWorld.Player.Position);
        if (gap > 10f)
            return $"the client's copy of the host drifted {gap:0.0} away over a lossy wire";
        return null;
    }

    /// <summary>
    /// The rubber band, caught in a test.
    ///
    /// A client predicts its own craft so the controls feel attached to something, and the
    /// host's snapshot then says where it really ended up. The trap is what the difference
    /// between the two is measured against: the snapshot describes the craft as it was when
    /// the host stepped this client's input, which is a full round trip ago, so a MOVING craft
    /// is always about (speed x round trip) ahead of it and always will be. Correcting toward
    /// it therefore drags the craft backward the whole time the player drives — at every ping,
    /// for ever, with nothing wrong. That is the single thing a laggy-feeling client feels.
    ///
    /// The fix is to measure the error against this machine's own prediction FOR THE SAME TICK
    /// the host is describing, which the host names on every players packet. A prediction that
    /// was right then costs nothing now, however bad the wire is.
    ///
    /// So: the same craft, the same keys, from the same spot, twice — once with no network at
    /// all, and once as a client half a second of round trip away from the host. They have to
    /// end up in the same place.
    /// </summary>
    private static string? PredictionIsNotDraggedBackwardsByLatency()
    {
        // A quarter of a second each way and nothing else — no loss, no jitter — so what this
        // measures is the reconciliation rather than the wire.
        var link = new LinkQuality(15, 0, 0f);
        Vector2 start = Torus.Wrap(new Vector2(-60f, -60f));
        const float Aim = 0.9f;
        const int Ticks = 300;                                  // five seconds of driving
        var drive = new InputFrame(Btn.Forward, Btn.None, Vector2.Zero);

        // The answer key: no wire in the picture at all. Wherever this craft ends up is where
        // the player's hands asked to go.
        var solo = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        solo.Enemies.Clear();
        solo.Pickups.Clear();
        solo.Player.Position = start;
        solo.Player.Heading = Aim;
        for (int i = 0; i < Ticks; i++) solo.StepForTest((float)Config.FixedDt, drive);
        float soloWent = Torus.Distance(solo.Player.Position, start);
        if (soloWent < 40f) return $"the reference craft only drove {soloWent:0.0} in five seconds";

        var net = new LoopbackNet(2, link, seed: 90210);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        hostWorld.Pickups.Clear();

        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();
        clientWorld.Pickups.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        // Let the handshake land across a slow wire before anything is measured.
        for (int i = 0; i < 90; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hostWorld.StepForTest((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.StepForTest((float)Config.FixedDt, InputFrame.Empty);
        }
        if (client.LocalSeat != 1) return "the client never got a seat";

        // Both ends of the client's craft put on the reference craft's mark, and the host's own
        // craft parked well out of the way so it cannot be driven into.
        hostWorld.Players[0].Position = Torus.Wrap(new Vector2(140f, 140f));
        foreach (var w in new[] { hostWorld, clientWorld })
        {
            w.Players[1].Position = start;
            w.Players[1].Heading = Aim;
        }

        for (int i = 0; i < Ticks; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(drive);
            hostWorld.StepForTest((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.StepForTest((float)Config.FixedDt, drive);
        }

        // The one that matters: what the player is looking at on their own screen. Half a
        // second of round trip at this speed is about thirteen units of travel, so a craft
        // being dragged onto the stale position lands that far short — several times this bar.
        float shortfall = Torus.Distance(clientWorld.Players[1].Position, solo.Player.Position);
        if (shortfall > 3f)
            return $"the client's own craft ended up {shortfall:0.0} from where the same keys "
                 + "drove it with no wire — it is being dragged backwards by the latency";

        // And the host agrees, which is what stops the above being achieved by simply ignoring
        // the host. The host is a one-way trip behind by construction, hence the looser bar.
        float apart = Torus.Distance(hostWorld.Players[1].Position, solo.Player.Position);
        if (apart > 12f)
            return $"the host has the client's craft {apart:0.0} from where it drove itself";
        return null;
    }

    /// <summary>
    /// Where a player is looking is theirs.
    ///
    /// Aim used to cross the wire as a mouse DELTA the host integrated, and that cannot survive
    /// a channel that drops things: every lost packet took a permanent bite out of the host's
    /// idea of where this player was pointing, with nothing to ever put it back. The only thing
    /// that reconciled the two was the client hauling its own camera round onto the host's
    /// stale heading — a hard snap past forty degrees, which a fast flick at any real ping
    /// clears easily. Being unable to turn without the view being wrenched back is as close to
    /// unplayable as this game got.
    ///
    /// Now the angle itself crosses and the host takes it at its word, so the client's aim is
    /// never touched at all. This drives a long, hard turn across a bad wire and insists the
    /// player's own view is exactly what their hand asked for, to the last radian.
    /// </summary>
    private static string? ClientAimIsNeverWrenchedBack()
    {
        var net = new LoopbackNet(2, LinkQuality.Awful, seed: 777);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        for (int i = 0; i < 90; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (client.LocalSeat != 1) return "the client never got a seat";

        // The same hand, turning the same craft, with nothing on the other end of it. This is
        // what the player asked for; anything else the networked craft does is somebody else
        // moving their view.
        var solo = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        solo.Enemies.Clear();

        // A steady hard swing — the mouse moved a long way every tick for two seconds, which
        // is the motion the old snap threshold could not survive.
        var flick = new InputFrame(Btn.None, Btn.None, new Vector2(9f, 0f));
        PlayerTank mine = clientWorld.Players[1];
        mine.Heading = 0f;
        solo.Player.Heading = 0f;

        for (int i = 0; i < 120; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(flick);
            hostWorld.Update((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.Update((float)Config.FixedDt, flick);
            solo.Update((float)Config.FixedDt, flick);
        }

        float expected = solo.Player.Heading;
        float drift = MathF.Abs(MathF.IEEERemainder(mine.Heading - expected, MathF.Tau));
        if (drift > 1e-3f)
            return $"the player's own view was moved {drift:0.000} rad by something that was "
                 + "not their hand";

        // The turn was a real one and not a rounding error, or the above proves nothing.
        if (MathF.Abs(MathF.IEEERemainder(expected, MathF.Tau)) < 0.5f)
            return "the test never actually turned the craft far enough to matter";

        // Mid-turn the host is legitimately behind — a third of a second of wire is a third of
        // a second of turn it has not been told about yet, and no scheme can fix that. What
        // matters is what is left when the hand stops: let the wire drain and the host has to
        // land EXACTLY where the player is looking.
        //
        // This is the half a mouse delta could not do. A delta is a contribution, so a dropped
        // packet is a slice of the turn subtracted from the host's total with nothing to ever
        // add it back — the two ends settle a permanent distance apart, and further losses
        // widen it without limit. An angle is a statement of fact, so one surviving packet puts
        // the host exactly right no matter how many were lost before it.
        for (int i = 0; i < 150; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (net.Dropped == 0) return "the wire never actually dropped anything to recover from";

        float gap = MathF.Abs(MathF.IEEERemainder(hostWorld.Players[1].Heading - mine.Heading,
                                                  MathF.Tau));
        if (gap > 0.01f)
            return $"after the turn ended and the wire drained, the host still has this craft "
                 + $"pointing {gap:0.000} rad away from its player — the disagreement is permanent";

        // And the client's own view still has not been touched by any of it.
        float settled = MathF.Abs(MathF.IEEERemainder(mine.Heading - expected, MathF.Tau));
        if (settled > 1e-3f)
            return $"the player's view was pulled {settled:0.000} rad off its own line while the "
                 + "wire caught up";
        return null;
    }

    /// <summary>
    /// One press is one shot, even when the packet carrying it never arrives.
    ///
    /// A frame carries EDGES — the single tick a key went down — and it went out unreliable,
    /// alone, once. So a dropped packet was an action the player took that simply never
    /// happened. The other half was worse: the host held the last frame it received and re-ran
    /// it on every tick nothing arrived, edges and all, so the same lost packet could just as
    /// easily fire the thing twice.
    ///
    /// Both halves are exercised here against a wire that is switched off outright — not made
    /// lossy, switched off — around a single press of the slug. It has to fire exactly once.
    /// </summary>
    private static string? ALostInputPacketCostsNoActionAndDuplicatesNone()
    {
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 31337);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        for (int i = 0; i < 30; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (client.LocalSeat != 1) return "the client never got a seat";

        var slug = new InputFrame(Btn.TankSlug, Btn.TankSlug, Vector2.Zero);
        int ammoBefore = hostWorld.Players[1].Ammo;

        // Every round the host ever spawns for this seat, counted once each at the moment it
        // first appears — the honest count of how many times the press was acted on, which
        // neither ammo nor a live projectile list can give (a slug flies off and expires).
        var seen = new HashSet<object>();
        int slugs = 0;

        // The press itself, and then a hundred and twenty ticks of silence from this client:
        // long past the slug's own cooldown, so a host repeating the edge frame has every
        // opportunity to fire a second one. The mute covers the press tick as well, so the
        // packet that carried the edge is genuinely gone and only the redundant copies in the
        // packets behind it can save the shot.
        for (int i = 0; i < 200; i++)
        {
            net.Advance();
            bool pressing = i == 0;
            net.Mute(1, i < 3);            // the press's own packet, and the two behind it
            host.Pump(InputFrame.Empty);
            client.Pump(pressing ? slug : InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, InputFrame.Empty);
            clientWorld.Update((float)Config.FixedDt, pressing ? slug : InputFrame.Empty);

            foreach (var r in hostWorld.Projectiles)
                if (r.Owner == 1 && seen.Add(r)) slugs++;
        }

        if (slugs == 0)
            return "the press was lost with the packet that carried it — the redundant copies "
                 + "in the packets behind it did not save it";
        if (slugs > 1)
            return $"one press fired {slugs} rounds: the host is repeating the edges of a frame "
                 + "it is only holding because nothing newer arrived";

        int spent = ammoBefore - hostWorld.Players[1].Ammo;
        if (spent != PlayerTank.SlugAmmoCost)
            return $"one slug should cost {PlayerTank.SlugAmmoCost} rounds, and this cost {spent}";
        return null;
    }

    /// <summary>
    /// A remote craft carries on through a missed packet instead of standing still.
    ///
    /// Easing a body onto the host's last reported position turns twenty reports a second into
    /// a glide, which is most of the job. What it got wrong was the packet that never came: the
    /// target stopped moving, so the body eased onto it and PARKED until the next one landed,
    /// then lurched. On a wire dropping one packet in twenty that is a hitch about once a
    /// second on every craft and every hunter on screen simultaneously, and it reads exactly
    /// like lag — which it is not. The host knows where that craft is and the client has
    /// everything it needs to work out where it is heading.
    ///
    /// So the target coasts along the speed the last two reports implied. This drives a craft
    /// in a straight line, cuts the wire dead for a tenth of a second, and insists it keeps
    /// going.
    /// </summary>
    private static string? ARemoteCraftCoastsThroughALostPacket()
    {
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 5150);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);

        // The host drives its own craft in a straight line; the client only ever watches it.
        var drive = new InputFrame(Btn.Forward, Btn.None, Vector2.Zero);
        for (int i = 0; i < 240; i++)
        {
            net.Advance();
            host.Pump(drive);
            client.Pump(InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, drive);
            clientWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (clientWorld.Players.Count < 2) return "the client never learned about the host's craft";

        PlayerTank seenHere = clientWorld.Players[0];
        if (!seenHere.HasNet) return "the client is not being told about the host's craft at all";

        // Now the wire dies for six ticks — a tenth of a second, two whole snapshots gone. This
        // is a mute rather than a lossy link so the gap is exact and the test cannot pass by
        // luck.
        Vector2 wasAt = seenHere.Position;
        net.Mute(0, true);
        for (int i = 0; i < 6; i++)
        {
            net.Advance();
            host.Pump(drive);
            client.Pump(InputFrame.Empty);
            hostWorld.Update((float)Config.FixedDt, drive);
            clientWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        net.Mute(0, false);

        // Six ticks at the tank's pace is about two and a half units. A craft that stalled has
        // eased onto a stationary target and travelled a small fraction of that; one that
        // coasted has kept very nearly the host's real speed.
        float went = Torus.Distance(seenHere.Position, wasAt);
        if (went < 1.5f)
            return $"with the wire cut for a tenth of a second the remote craft moved {went:0.00} "
                 + "— it is standing still between packets rather than carrying on";

        // And it did not run away with itself: the coast is a guess, and a guess that outruns
        // the thing it is guessing about is worse than the stall it replaced.
        float ahead = Torus.Distance(seenHere.Position, hostWorld.Players[0].Position);
        if (ahead > 6f)
            return $"the coasting craft ended up {ahead:0.0} from where it really is";
        return null;
    }

    private static string? JoinCodesRoundTrip()
    {
        // The one thing a host reads down a phone. If this is asymmetric nobody can ever
        // connect, and the failure looks like a networking problem rather than a typo.
        uint[] ids = [0u, 1u, 28u, 29u, 357738826u, 1234567890u, uint.MaxValue];
        foreach (uint id in ids)
        {
            string code = Net.SteamNet.Encode(id);
            if (code.Length != 7) return $"account {id} encoded to {code.Length} characters";

            var back = Net.SteamNet.Decode(code);
            if (back is null) return $"the code {code} would not decode at all";
            if (back.Value.GetAccountID().m_AccountID != id)
                return $"{id} encoded to {code} and came back as {back.Value.GetAccountID().m_AccountID}";
        }

        // Read out loud and typed back in: lower case, and with the spaces the screen shows
        // between the characters. Both have to survive or half of them will not connect.
        string spaced = string.Join(' ', Net.SteamNet.Encode(357738826u).ToCharArray()).ToLowerInvariant();
        if (Net.SteamNet.Decode(spaced)?.GetAccountID().m_AccountID != 357738826u)
            return "a code typed back with spaces and in lower case was rejected";

        // And nonsense stays rejected rather than dialling somebody at random.
        if (Net.SteamNet.Decode("AAAA") != null) return "a four-character code was accepted";
        if (Net.SteamNet.Decode("AEIOU01") != null) return "a code full of excluded letters was accepted";
        return null;
    }

    private static string? SoundCrossesTheWire()
    {
        // The heart of audio parity: a cue the host raises has to reach a client and be played
        // there, positioned to the client's own craft. Headless there is no audio device, so
        // this asserts the cue crossed the wire and was taken up (RemoteCuesPlayed), and that a
        // client's OWN cue is not echoed back at it.
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 55);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        if (client.LocalSeat != 1) return $"the joiner never seated (at {client.LocalSeat})";

        // The host raises a world sound near the origin, where both seats opened.
        hostWorld.Emit(Cue.Explosion, hostWorld.Players[1].Position);
        // ...and the client fires its own shot, whose echo must not come back to it.
        hostWorld.Emit(Cue.Detonation, hostWorld.Players[1].Position, owner: 1);

        for (int i = 0; i < 12; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
        }

        if (clientWorld.RemoteCuesPlayed < 1)
            return "the host raised a sound and the client never heard it";
        // Exactly the explosion should have crossed — the client's own detonation is skipped.
        if (clientWorld.RemoteCuesPlayed != 1)
            return $"the client played {clientWorld.RemoteCuesPlayed} cues; its own shot should not echo back";
        return null;
    }

    private static string? PickupsCrossTheField()
    {
        // Salvage rides the field packet so a client sees the cells and shards on the grid.
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        host.Pickups.Clear();
        host.Pickups.Add(new Pickup(Torus.Wrap(new Vector2(5f, 5f)), PickupKind.Battery));
        host.Pickups.Add(new Pickup(Torus.Wrap(new Vector2(-3f, 8f)), PickupKind.Ammo));

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        client.Pickups.Clear();

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WriteField(host, forSeat: 0, tick: 1u, buf);
        Snapshot.ApplyField(client, buf.AsSpan(0, n));

        if (client.Pickups.Count != 2) return $"the client drew {client.Pickups.Count} of 2 pickups";
        if (client.Pickups[0].Kind != PickupKind.Battery) return "a pickup arrived as the wrong kind";
        return null;
    }

    /// <summary>
    /// A field packet holds a bounded amount of salvage, and it used to be whichever of it
    /// happened to be at the front of the host's list — which is arrival order. Once kills
    /// started scattering parts, a firefight would fill that budget with everything the player
    /// had already walked past, and the pile they were actually standing in was never sent at
    /// all: salvage that existed, that the host would have handed over, and that there was
    /// nothing on screen to drive over. The packet now carries the nearest, so what a player
    /// can see is what is there.
    /// </summary>
    private static string? ClientSeesTheNearestSalvage()
    {
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        host.Pickups.Clear();
        Vector2 eye = host.Players[0].Position;

        // A great deal of old salvage, far out. More than any packet will carry.
        for (int i = 0; i < 60; i++)
        {
            float a = MathF.Tau * i / 60f;
            host.Pickups.Add(new Pickup(
                Torus.Wrap(eye + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 140f),
                PickupKind.Ammo));
        }
        // And, dropped last, the parts off a body at the player's feet.
        host.Pickups.Add(new Pickup(Torus.Wrap(eye + new Vector2(1.5f, 0f)), PickupKind.ScrapMetal));
        host.Pickups.Add(new Pickup(Torus.Wrap(eye + new Vector2(0f, 1.5f)), PickupKind.CopperWire));

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        client.Pickups.Clear();

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WriteField(host, forSeat: 0, tick: 1u, buf);
        Snapshot.ApplyField(client, buf.AsSpan(0, n));

        bool scrap = false, wire = false;
        foreach (var pk in client.Pickups)
        {
            scrap |= pk.Kind == PickupKind.ScrapMetal;
            wire |= pk.Kind == PickupKind.CopperWire;
        }
        if (!scrap || !wire)
            return $"the parts at the player's feet never crossed ({client.Pickups.Count} pickups did)";

        // And the kind travels with the position: the host's nearest salvage is re-sorted
        // every packet, so a cell reused for a different piece must take its new identity.
        // Two packets in a row, with the salvage swapped underneath, and the client's list
        // must describe the second one rather than the first.
        foreach (var pk in host.Pickups) pk.NetSet(pk.Position, PickupKind.SpaceGunpowder);
        n = Snapshot.WriteField(host, forSeat: 0, tick: 2u, buf);
        Snapshot.ApplyField(client, buf.AsSpan(0, n));
        foreach (var pk in client.Pickups)
            if (pk.Kind != PickupKind.SpaceGunpowder)
                return $"a reused pickup kept its old kind ({pk.Kind})";
        return null;
    }

    /// <summary>
    /// Salvage that lands inside a building can be seen from across the field and never
    /// reached: a tower is solid, and the craft is stopped at its footprint. Every drop is
    /// nudged clear of the wall, and clear by enough that the craft's own radius still fits.
    /// </summary>
    private static string? SalvageNeverLandsInsideAWall()
    {
        var world = new World.World { DynamicSpawning = false };
        if (world.Structures.Count == 0) return "the world seeded no buildings to test against";

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        int tested = 0;
        foreach (var s in world.Structures)
        {
            int n = s.Blockers(blockers);
            for (int i = 0; i < n; i++)
            {
                // Dead centre, and a little off it: both have to come back out.
                foreach (var probe in new[] { blockers[i].At, Torus.Wrap(blockers[i].At + new Vector2(1f, 0.5f)) })
                {
                    Vector2 placed = world.ReachablePoint(probe);
                    float d = MathF.Sqrt(Torus.DistanceSquared(placed, blockers[i].At));
                    if (d < blockers[i].Radius + Entities.PlayerTank.Radius)
                        return $"salvage was placed {d:0.0} into a wall of radius {blockers[i].Radius:0.0}";
                    tested++;
                }
            }
            if (tested > 40) break;   // a representative sweep, not the whole skyline
        }
        return tested == 0 ? "no building offered a footprint to test" : null;
    }

    /// <summary>
    /// Two players clearing two pieces of salvage on the same tick. Only one of them used to
    /// actually leave the field — the other stayed, and went on paying out its contents every
    /// tick for as long as anybody stood on it.
    /// </summary>
    private static string? SalvageIsSpentWhenTaken()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Pickups.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1

        world.Players[0].Position = Torus.Wrap(new Vector2(-60f, 0f));
        world.Players[1].Position = Torus.Wrap(new Vector2(60f, 0f));
        world.Pickups.Add(new Pickup(world.Players[0].Position, PickupKind.ScrapMetal));
        world.Pickups.Add(new Pickup(world.Players[1].Position, PickupKind.ScrapMetal));

        // Several ticks: a piece that was taken but never removed keeps paying out.
        for (int i = 0; i < 5; i++) StepWithoutInput(world);

        if (world.Pickups.Count != 0)
            return $"{world.Pickups.Count} taken pickups were left lying on the field";
        int a = CountItems(world.InventoryOf(0), ItemKind.ScrapMetal);
        int b = CountItems(world.InventoryOf(1), ItemKind.ScrapMetal);
        if (a != 1 || b != 1) return $"two pieces of salvage paid out {a} and {b}, expected 1 each";
        return null;
    }

    private static string? BossesCrossTheWire()
    {
        // A client must SEE another player's boss fight, not only hear it. The host's Crab-Core
        // and Maw-Core go out as their own tiny packet and arrive as render-only puppets at the
        // right place and phase; when the host stops reporting one, the puppet is dropped.
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        host.SpawnCrabAhead();
        host.AttachMawForTest(new MawCore(Torus.Wrap(new Vector2(12f, 9f))));
        host.Soldiers.Add(new EnemySoldier(Torus.Wrap(new Vector2(6f, 4f)), 3f, leader: true, slot: 0));
        host.Soldiers.Add(new EnemySoldier(Torus.Wrap(new Vector2(8f, 5f)), 2f, leader: false, slot: 1));

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;

        var buf = new byte[64];
        int n = Snapshot.WriteBosses(host, forSeat: 0, tick: 3u, buf);
        Snapshot.ApplyBosses(client, buf.AsSpan(0, n));

        if (client.Boss is not { IsPuppet: true }) return "the client never received the Crab-Core";
        if (client.Maw is not { IsPuppet: true }) return "the client never received the Maw-Core";
        if (Torus.Distance(client.Boss.Position, host.Boss!.Position) > 0.6f)
            return "the crab puppet landed at the wrong place";
        if (client.Boss.Phase != host.Boss.Phase)
            return $"the crab puppet is in {client.Boss.Phase}, not the host's {host.Boss.Phase}";
        if (client.Soldiers.Count != 2)
            return $"the client drew {client.Soldiers.Count} of the host's 2 squad members";
        if (!client.Soldiers[0].IsLeader)
            return "the squad leader arrived unmarked on the client";

        // A world with no bosses drops the puppets.
        var empty = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        empty.Enemies.Clear();
        int n2 = Snapshot.WriteBosses(empty, forSeat: 0, tick: 4u, buf);
        Snapshot.ApplyBosses(client, buf.AsSpan(0, n2));
        if (client.Boss != null || client.Maw != null || client.Soldiers.Count != 0)
            return "the host dropped its bosses/squad and the client kept the puppets";
        return null;
    }

    private static string? ClientDoesNotSimulateTheField()
    {
        // A client is shown the field, it does not run it. Stepped on its own with a hunter and
        // a boss placed, an authoritative world would advance them; a client world must leave
        // everything it does not drive exactly where the last snapshot put it, and raise no cues
        // of its own for it.
        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.Enemies.Clear();
        var enemy = new EnemyTank(Torus.Wrap(new Vector2(20f, 0f)), elite: false);
        client.Enemies.Add(enemy);
        Vector2 before = enemy.Position;

        for (int i = 0; i < 30; i++) client.StepForTest((float)Config.FixedDt);

        if (Torus.Distance(enemy.Position, before) > 0.01f)
            return "a client stepped a hunter it should only have been shown";
        return null;
    }

    private static string? PickInstallsChosenChassis()
    {
        // The bug: chassis is chosen in the 3D room, after connecting, so a client is seated as
        // a placeholder tank and its own craft has to be rebuilt when it picks. If it is not,
        // the client drives a tank while the host simulates the chassis it actually chose, and
        // the two move so differently the craft is never where anyone believes it is. This
        // proves a pick lands on BOTH ends: the client's own seat and the host's copy of it.
        var net = new LoopbackNet(2, LinkQuality.Typical, seed: 314);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 });
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 });

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);   // placeholder — the real pick comes from the pod

        var hostRoom = new World.LobbyRoom { IsHost = true };
        hostRoom.Seat(0, "HOST");
        var clientRoom = new World.LobbyRoom();

        // Seat the joiner, exactly as the loop drives it on the lobby screen.
        for (int i = 0; i < 80; i++)
        {
            net.Advance();
            host.LobbyTick(hostRoom);
            if (client.LocalSeat >= 0 && clientRoom.Stage != World.LobbyRoom.Phase.InRoom)
                clientRoom.Seat(client.LocalSeat, "JOINER");
            client.LobbyTick(clientRoom);
        }
        if (client.LocalSeat != 1) return $"the joiner seated at {client.LocalSeat}, not 1";

        // The joiner walks to the pod and picks a SOLDIER.
        clientRoom.PickForTest(PlayerClass.Soldier);
        for (int i = 0; i < 80; i++)
        {
            net.Advance();
            host.LobbyTick(hostRoom);
            client.LobbyTick(clientRoom);
        }

        if (clientWorld.Players[1].Soldier is null)
            return $"the client picked a soldier and is still driving a {clientWorld.Players[1].Class}";
        if (hostWorld.Players[1].Soldier is null)
            return $"the host has the joiner as a {hostWorld.Players[1].Class}, not the soldier they picked";
        return null;
    }

    private static string? LobbyHandshakeSeatsAJoiner()
    {
        // The bug this exists to prevent: the wire was only turned once a match was running,
        // so two machines both sitting on the lobby screen never exchanged a word. The
        // connection came up, the joiner said hello into it, and the host — which was not
        // draining its socket until it was already playing — never answered. It looked
        // exactly like a network failure and was nothing of the kind.
        //
        // So: nothing here steps a world or pumps a tick. Only what the lobby itself runs.
        var net = new LoopbackNet(2, LinkQuality.Typical, seed: 99);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 5 });
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 });

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Fish);

        for (int i = 0; i < 120; i++)
        {
            net.Advance();
            host.PumpLobby();
            client.PumpLobby();
        }

        if (client.LocalSeat != 1)
            return $"two seconds on the lobby screen and the joiner was still seated at {client.LocalSeat}";
        if (hostWorld.Players.Count != 2) return "the host never seated the joiner";
        if (hostWorld.Players[1].Fish is null)
            return "the joiner asked for a fish and the host seated something else";

        // The host's rules reach the client with the seat, so both ends agree before anyone
        // is in the world.
        if (client.World!.Match.Revives != 5)
            return $"the client thinks the match has {client.World.Match.Revives} revives, not 5";

        // And being seated is not being in: the client waits on LAUNCH.
        if (client.MatchStarted) return "the client walked in before the host started the match";

        host.StartMatch();
        for (int i = 0; i < 60; i++)
        {
            net.Advance();
            host.PumpLobby();
            client.PumpLobby();
        }
        if (!client.MatchStarted) return "the host pressed LAUNCH and the client never heard";
        return null;
    }

    private static string? WorldIsAFiniteTorus()
    {
        // Drive off one edge and you come back on the opposite one — the map is not
        // infinite. A point a full world-width along any axis is literally the same
        // point, so its wrapped distance is zero.
        if (Torus.Distance(new Vector2(12f, -30f), new Vector2(12f + Torus.Size, -30f)) > 0.001f)
            return "a full world-width apart isn't the same place";

        // Wrapping always folds a coordinate back into the [-Half, Half) play window,
        // so positions can never run off to infinity.
        float w = Torus.WrapCoord(Torus.Half + 5f);
        if (w < -Torus.Half || w >= Torus.Half)
            return $"a wrapped coordinate ({w:F1}) fell outside the world window";

        // The opposite edges are stitched together: just over the top edge is a couple
        // of units below the bottom edge, not a whole arena away.
        float seam = Torus.Distance(new Vector2(0f, Torus.Half - 1f),
                                    new Vector2(0f, -Torus.Half + 1f));
        if (seam > 3f) return $"the seam isn't stitched — edge to edge measured {seam:F1}";

        // And a craft driven straight past the world's edge reappears wrapped, still
        // in the window, rather than sailing off forever.
        var player = new Entities.PlayerTank(new Vector2(Torus.Half - 2f, 0f));
        player.Position = Torus.Wrap(player.Position + new Vector2(6f, 0f));
        if (player.Position.X > 0f)
            return "crossing the +X edge didn't wrap the craft to the far side";
        return null;
    }

    /// <summary>
    /// A tower comes apart <em>where it is cut</em>, not as one rigid topple. A hard hit at the
    /// crown has to take the top off and leave the base standing — still fractured, still a
    /// solid blocker — while a hit at the base pulls the supports out from under everything and
    /// the whole thing comes down. Driven against the chunk model directly, so it pins the
    /// localized-collapse behaviour without needing to aim a beam up a tower headlessly.
    /// </summary>
    private static string? LocalizedFractureSparesTheBase()
    {
        // A standalone tower, off the field, so it can be cut anywhere without the clearing
        // radius or the seeded layout in the way.
        var tower = new Structure(Vector2.Zero, 0f, StructureKind.Tower, variant: 0, scale: 1f);
        var loosed = new List<DebrisSpawn>();

        // Cut hard up at the crown: the top must come off, the base must stand.
        bool crownFelled = tower.CutTower(new Vector3(0f, 40f, 0f), 6f, 1e6f, loosed, out bool first);
        if (crownFelled) return "a hit at the crown brought the whole tower down";
        if (!first) return "the first cut didn't fracture the tower";
        if (tower.Falling) return "a crown hit put the whole tower into collapse";
        if (tower.Fracture is not { AnyStanding: true }) return "the base didn't survive a crown hit";
        if (loosed.Count == 0) return "a crown hit knocked nothing loose";

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        if (tower.Blockers(blockers) == 0) return "a half-standing tower stopped being solid";

        // Now cut the base out: with its supports gone the whole thing has to come down, and
        // once its last cell is clear it leaves the field.
        bool baseFelled = tower.CutTower(new Vector3(0f, 2f, 0f), 6f, 1e6f, loosed, out _);
        if (!baseFelled) return "cutting the base left the tower standing";
        if (!tower.Falling) return "the base was cut but the tower isn't coming down";
        if (tower.Blockers(blockers) != 0) return "a collapsing tower is still solid";
        return null;
    }

    /// <summary>
    /// A collapsing structure's rubble is not decoration: a chunk coming down on a character
    /// crushes it. A hunter is stood on open grid, and a real mass-bearing chunk — the kind a
    /// felled tower throws — is steered straight down onto it. One step of the crush pass has
    /// to bill it and kill it, which proves the whole path is wired: mass on the shard, the
    /// world reading it back, and the hit reaching the enemy.
    /// </summary>
    private static string? FallingRubbleCrushesCharacters()
    {
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();
        var mark = new Vector2(30f, 12f);   // clear of the origin, clear of any tower footprint
        world.Enemies.Add(new Entities.EnemyTank(mark, elite: false));

        // Throw a burst of structural rubble so the pool holds mass-bearing chunks, then steer
        // one of them directly over the hunter, coming straight down at a section's speed —
        // exactly the moment a piece of a felled tower would arrive on something beneath it.
        world.Debris.Rubble(new Vector3(mark.X, 6f, mark.Y), Palette.StructureShell, chunks: 8, scale: 2f);

        var shards = world.Debris.Shards;
        bool placed = false;
        for (int i = 0; i < shards.Length; i++)
        {
            if (!shards[i].Active || shards[i].Mass <= 0f) continue;
            shards[i].Position = new Vector3(mark.X, 1.5f, mark.Y);
            shards[i].Velocity = new Vector3(0f, -14f, 0f);
            placed = true;
            break;
        }
        if (!placed) return "no mass-bearing rubble was spawned to test the crush";

        StepWithoutInput(world);   // Debris.Update moves it, then ResolveCrush bills it
        return world.Enemies.Count == 0
            ? null : "a chunk came down on a hunter and it walked away";
    }

    private static string? PlayerKillsEnemy()
    {
        var world = new World.World { DynamicSpawning = false };
        // Face the enemy and hold fire; step a few seconds of sim. Spawning is off so
        // the seeded hunter is the only one — killing it clears the field.
        AimPlayerAtFirstEnemy(world);

        for (int i = 0; i < 60 * 8 && world.Enemies.Count > 0; i++)
        {
            AimPlayerAtFirstEnemy(world);
            world.FirePlayerShot();
            StepWithoutInput(world);
        }
        return world.Enemies.Count == 0 ? null : "enemy still alive after 8s of fire";
    }

    /// <summary>
    /// Every kill now leaves salvage at the corpse — a battery or a handful of rounds — so
    /// clearing a firefight feeds the craft that cleared it. Killing the seeded hunter has to
    /// add exactly one pickup, and it has to be one of the two usable kinds.
    /// </summary>
    /// <summary>
    /// A kill leaves exactly one reward — a battery or a handful of rounds — and, on top of
    /// it, whatever parts the body gave up: a machine that has just been destroyed comes
    /// apart, and the scrap, powder and wire the workshop runs on is where that comes from.
    /// Run over several kills, because the parts are rolled per material and a single body is
    /// entitled to leave none of them.
    /// </summary>
    private static string? KillsLeaveSalvage()
    {
        const int Kills = 8;
        int killed = 0, scrapSeen = 0;

        for (int round = 0; round < Kills; round++)
        {
            var world = new World.World { DynamicSpawning = false };
            world.Pickups.Clear();

            // Where the hunter was standing on the last tick it was alive — the parts are
            // scattered about the body, and this is the only handle on where that was.
            Vector2 grave = world.Enemies.Count > 0 ? world.Enemies[0].Position : Vector2.Zero;
            for (int i = 0; i < 60 * 8 && world.Enemies.Count > 0; i++)
            {
                AimPlayerAtFirstEnemy(world);
                grave = world.Enemies[0].Position;
                world.FirePlayerShot();
                StepWithoutInput(world);
            }
            if (world.Enemies.Count > 0) continue;   // never died; not this round's business
            killed++;

            int reward = 0, parts = 0;
            foreach (var pk in world.Pickups)
            {
                switch (pk.Kind)
                {
                    // The three things a kill can reward: rounds for the gun, a cell for the
                    // shields, or — rarely — the kit that is the only thing that mends hull.
                    case Entities.PickupKind.Battery:
                    case Entities.PickupKind.Ammo:
                    case Entities.PickupKind.RepairKit:
                        reward++;
                        break;
                    case Entities.PickupKind.ScrapMetal:
                    case Entities.PickupKind.SpaceGunpowder:
                    case Entities.PickupKind.CopperWire:
                        parts++;
                        if (pk.Kind == Entities.PickupKind.ScrapMetal) scrapSeen++;
                        // Close enough to the corpse to be swept up in one pass. Generous
                        // against the scatter radius, because the body kept moving right up
                        // to the tick it died.
                        float d = MathF.Sqrt(Torus.DistanceSquared(pk.Position, grave));
                        if (d > World.World.ScatterRadius + 4f)
                            return $"a part landed {d:0.0} from the body it came off";
                        break;
                    default:
                        return $"a kill dropped a {pk.Kind}";
                }
            }
            if (reward != 1) return $"a kill left {reward} battery/ammo drops, expected 1";
            if (parts > 3) return $"a kill left {parts} parts, expected at most 3";
        }

        if (killed == 0) return "no enemy died in 8s of fire";
        // Scrap comes off nine bodies in ten, so eight kills with none at all is not luck.
        if (scrapSeen == 0) return $"{killed} kills left no scrap metal at all";
        return null;
    }

    private static string? EnemyDamagesPlayer()
    {
        var world = new World.World { DynamicSpawning = false };
        float startShield = world.Player.Shield;

        // Don't fire; just let the enemy close and shoot. Player stays grounded
        // (no jump) so hits land.
        for (int i = 0; i < 60 * 15; i++)
        {
            AimPlayerAtFirstEnemy(world);
            StepWithoutInput(world);
            if (world.Player.Shield < startShield) break;
        }
        return world.Player.Shield < startShield
            ? null
            : "player took no damage in 15s under fire";
    }

    /// <summary>
    /// A leaping craft used to be untouchable — enemy fire passed clean underneath the
    /// moment a wheel left the grid. Now the hunters elevate onto the craft's height, so a
    /// player held up in the air still takes fire. Same setup as
    /// <see cref="EnemyDamagesPlayer"/>, but the craft is pinned six metres up: if the old
    /// blanket immunity were still in force nothing here would ever land.
    /// </summary>
    private static string? EnemyReachesPlayerInTheAir()
    {
        var world = new World.World { DynamicSpawning = false };
        float startShield = world.Player.Shield;
        const float altitude = 6f;

        for (int i = 0; i < 60 * 15; i++)
        {
            // Hold it aloft before the step (so the hunters aim up at it) and again after
            // (so the jump physics inside the step don't quietly bring it back down).
            world.Player.Height = altitude;
            AimPlayerAtFirstEnemy(world);
            StepWithoutInput(world);
            world.Player.Height = altitude;
            if (world.Player.Shield < startShield) break;
        }
        return world.Player.Shield < startShield
            ? null
            : "an airborne player took no damage in 15s under fire";
    }

    private static string? AmmoDepletes()
    {
        var world = new World.World();
        int startAmmo = world.Player.Ammo;
        for (int i = 0; i < 60 * 30; i++)
        {
            world.FirePlayerShot();
            StepWithoutInput(world);
            if (world.Player.Ammo == 0) break;
        }
        return world.Player.Ammo == 0 ? null : $"ammo did not deplete (left {world.Player.Ammo})";
    }

    private static string? BatteryStowsThenCharges()
    {
        var world = new World.World();
        // Spend some shield and hyper so a later charge has room to land.
        world.Player.TakeDamage(50f);
        world.Player.TryHyperspace(); // drains most of the Hyper reserve

        // A jump can land the craft inside a building, and the step's own collision pass
        // then slides it several metres clear of where it arrived — far enough to leave a
        // pickup dropped on the landing spot out of reach, which is nothing to do with what
        // this is testing. So the settling step happens first, and the battery goes down
        // where the craft actually ended up.
        StepWithoutInput(world);

        float shield0 = world.Player.Shield;
        float hyper0 = world.Player.Hyper;

        // Drop a battery right on the craft and step once so it's collected.
        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Battery));
        StepWithoutInput(world);

        // The new contract: salvage no longer charges on contact — it's stowed. Shield
        // has no passive regen, so it's the clean witness (Hyper trickles back on its
        // own every tick, which is not the battery charging).
        if (world.Player.Shield != shield0)
            return "battery charged shield on contact (should stow, not auto-charge)";
        if (CountItems(world.Inventory, ItemKind.Battery) < 1)
            return "battery was not stowed in the inventory";

        // Spending it (as the panel's right-click does) puts back one whole shield charge
        // and its share of hyper.
        int charges0 = world.Player.ChargesLeft;
        world.Player.ChargeShield(1);
        world.Player.RefillHyper(World.World.BatteryChargeFraction);
        if (world.Player.Shield <= shield0) return "spending a battery did not recharge shield";
        if (world.Player.ChargesLeft != charges0 + 1)
            return "spending one battery did not put back exactly one shield charge";
        if (world.Player.Hyper <= hyper0) return "spending a battery did not recharge hyper";
        return null;
    }

    private static string? AmmoStowsThenLoads()
    {
        var world = new World.World();
        // Burn some ammo first so a later reload is observable.
        for (int i = 0; i < 20; i++) { world.FirePlayerShot(); StepWithoutInput(world); }
        int ammo0 = world.Player.Ammo;

        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Ammo));
        StepWithoutInput(world);

        // Stowed, not auto-loaded, and carrying a random 5–20 rounds.
        if (world.Player.Ammo != ammo0) return "ammo loaded on contact (should stow, not auto-load)";
        int bullets = CountItems(world.Inventory, ItemKind.Bullet);
        if (bullets < 5 || bullets > 20) return $"bullet salvage stowed {bullets} rounds (expected 5-20)";

        // Spending the stack loads it into the magazine.
        world.Player.Ammo = Math.Min(world.Player.MaxAmmo, world.Player.Ammo + bullets);
        return world.Player.Ammo > ammo0 ? null : "spending bullets did not restock ammo";
    }

    private static string? FragmentsCraftCrabCore()
    {
        var inv = new Inventory();
        // Three fragments, one to a triangle corner, satisfy the recipe.
        for (int i = 0; i < Inventory.CraftCount; i++)
            inv.Craft[i] = new ItemStack(ItemKind.CrabFragment, 1);

        if (!inv.CanCraft()) return "three fragments did not satisfy the recipe";
        ItemStack core = inv.TakeCraftOutput();
        if (core.IsEmpty || core.Kind != ItemKind.CrabCore) return "crafting did not yield a CRAB CORE";
        // The fragments are spent — the corners are now empty.
        for (int i = 0; i < Inventory.CraftCount; i++)
            if (!inv.Craft[i].IsEmpty) return "crafting did not consume the fragments";
        return null;
    }

    /// <summary>
    /// The two new recipes: rounds out of powder, alloy and scrap, and a cell out of wire,
    /// scrap and any one of the three anode metals. The bench does not care which corner a
    /// part was dropped into, so both are checked in an order nobody would type by hand.
    /// </summary>
    private static string? MaterialsCraftRoundsAndCells()
    {
        var inv = new Inventory();
        inv.Craft[0] = new ItemStack(ItemKind.ScrapMetal, 1);
        inv.Craft[1] = new ItemStack(ItemKind.SpaceGunpowder, 1);
        inv.Craft[2] = new ItemStack(ItemKind.DenseAlloy, 1);

        ItemStack rounds = inv.CraftOutput();
        if (rounds.IsEmpty || rounds.Kind != ItemKind.Bullet)
            return "powder, alloy and scrap did not make rounds";
        if (rounds.Count < 2) return $"the round recipe yielded {rounds.Count}, expected a handful";
        if (inv.TakeCraftOutput().IsEmpty) return "claiming the rounds made nothing";
        foreach (var c in inv.Craft)
            if (!c.IsEmpty) return "crafting rounds did not consume the parts";

        // Each of the three metals satisfies the cell's third corner on its own.
        foreach (var metal in new[] { ItemKind.Lead, ItemKind.Zinc, ItemKind.Lithium })
        {
            var cell = new Inventory();
            cell.Craft[0] = new ItemStack(metal, 1);
            cell.Craft[1] = new ItemStack(ItemKind.ScrapMetal, 1);
            cell.Craft[2] = new ItemStack(ItemKind.CopperWire, 1);
            ItemStack made = cell.CraftOutput();
            if (made.IsEmpty || made.Kind != ItemKind.Battery)
                return $"wire, scrap and {metal} did not make a cell";
        }

        // And three parts that are not a recipe make nothing at all.
        var junk = new Inventory();
        junk.Craft[0] = new ItemStack(ItemKind.ScrapMetal, 1);
        junk.Craft[1] = new ItemStack(ItemKind.ScrapMetal, 1);
        junk.Craft[2] = new ItemStack(ItemKind.ScrapMetal, 1);
        return junk.CanCraft() ? "three scraps crafted something" : null;
    }

    /// <summary>
    /// The take-apart bench. A round always gives back the three things it is made of; a cell
    /// always gives back wire and scrap and, about half the time, one of the three metals.
    /// Rolled many times, because the point of the third arrow is that it is a gamble.
    /// </summary>
    private static string? TeardownYieldsParts()
    {
        // A round: three parts, every time, and always the same three.
        for (int i = 0; i < 20; i++)
        {
            var inv = new Inventory();
            inv.Break[0] = new ItemStack(ItemKind.Bullet, 1);
            if (!inv.BreakOne()) return "the bench refused to open a round";
            if (!inv.Break[0].IsEmpty) return "opening a round did not spend it";
            if (CountEverywhere(inv, ItemKind.SpaceGunpowder) != 1
                || CountEverywhere(inv, ItemKind.DenseAlloy) != 1
                || CountEverywhere(inv, ItemKind.ScrapMetal) != 1)
                return "a round did not come apart into powder, alloy and scrap";
        }

        // A cell: wire and scrap every time, a metal some of the time.
        int metals = 0;
        const int Cells = 200;
        for (int i = 0; i < Cells; i++)
        {
            var inv = new Inventory();
            inv.Break[0] = new ItemStack(ItemKind.Battery, 1);
            if (!inv.BreakOne()) return "the bench refused to open a cell";
            if (CountEverywhere(inv, ItemKind.CopperWire) != 1
                || CountEverywhere(inv, ItemKind.ScrapMetal) != 1)
                return "a cell did not always give back wire and scrap";
            int metal = CountEverywhere(inv, ItemKind.Lead)
                      + CountEverywhere(inv, ItemKind.Zinc)
                      + CountEverywhere(inv, ItemKind.Lithium);
            if (metal > 1) return "a cell gave back more than one metal";
            metals += metal;
        }
        // A coin toss over two hundred cells: anything outside this is a broken table, not
        // an unlucky run.
        if (metals < Cells / 4 || metals > Cells * 3 / 4)
            return $"{metals} of {Cells} cells gave up a metal — expected about half";

        // And raw material is the bottom of the pile: it does not come apart any further.
        var scrap = new Inventory();
        scrap.Break[0] = new ItemStack(ItemKind.ScrapMetal, 4);
        if (scrap.BreakOne()) return "the bench opened a scrap of metal";
        return null;
    }

    /// <summary>
    /// The two benches' placement rules. The corners take parts and nothing else, the bench
    /// takes only what can actually be opened, and the row the parts come down into is
    /// out-only — the player empties it, nothing may be dropped back in.
    /// </summary>
    private static string? BenchesTakeOnlyWhatTheyShould()
    {
        if (Inventory.Accepts(InvRegion.Craft, ItemKind.Battery))
            return "a battery was allowed into a crafting corner";
        if (!Inventory.Accepts(InvRegion.Craft, ItemKind.ScrapMetal))
            return "scrap was refused by a crafting corner";
        if (!Inventory.Accepts(InvRegion.Break, ItemKind.Battery))
            return "the take-apart bench refused a battery";
        if (Inventory.Accepts(InvRegion.Break, ItemKind.ScrapMetal))
            return "the take-apart bench accepted something it cannot open";
        if (Inventory.Accepts(InvRegion.Parts, ItemKind.ScrapMetal))
            return "the parts row accepted a drop";

        // Out of the parts row and into the grid works; the reverse does not.
        var inv = new Inventory();
        inv.Parts[0] = new ItemStack(ItemKind.ScrapMetal, 3);
        if (!inv.Move(InvRegion.Parts, 0, InvRegion.Slots, 0, 3))
            return "parts could not be dragged out into the grid";
        if (inv.Move(InvRegion.Slots, 0, InvRegion.Parts, 0, 3))
            return "the grid was allowed to push items back into the parts row";
        return CountItems(inv, ItemKind.ScrapMetal) == 3
            ? null : "dragging parts into the grid lost them";
    }

    private static string? CrabCoreBlastKills()
    {
        var world = new World.World();
        // Park an enemy just in front of the craft, well inside a lance's reach.
        world.Enemies.Clear();
        var enemy = new Entities.EnemyTank(world.Player.Position + world.Player.Forward * 8f, elite: false);
        world.Enemies.Add(enemy);

        // Equip a crafted core and throw it straight ahead; it lobs a short way, lands
        // near the enemy and erupts into the lance ring.
        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        // Step long enough for the bomb to detonate and the star to burn through.
        for (int i = 0; i < 180 && enemy.Alive; i++) StepWithoutInput(world);

        return enemy.Alive ? "the blast did not destroy the enemy in its path" : null;
    }

    private static string? CrabCoreBlastKillsBoss()
    {
        var world = new World.World();
        world.DynamicSpawning = false;
        world.Enemies.Clear();
        world.SpawnCrabAhead();   // a dormant Crab-Core out along the player's heading

        // Throw a core; it lobs out toward the boss and detonates near it.
        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        for (int i = 0; i < 240 && world.Boss is { Alive: true }; i++) StepWithoutInput(world);

        return world.Boss is null or { Alive: false }
            ? null : "the blast did not destroy the Crab-Core";
    }

    private static string? CrabCoreBlastKillsMaw()
    {
        var world = new World.World();
        world.DynamicSpawning = false;
        world.Enemies.Clear();
        world.SpawnMawAhead();    // a hanging Maw-Core out along the player's heading

        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        for (int i = 0; i < 240 && world.Maw is { Alive: true }; i++) StepWithoutInput(world);

        return world.Maw is null or { Alive: false }
            ? null : "the blast did not destroy the Maw-Core";
    }

    /// <summary>Total count of a kind across the inventory grid.</summary>
    private static int CountItems(Inventory inv, ItemKind kind)
    {
        int n = 0;
        foreach (var s in inv.Slots)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        return n;
    }

    /// <summary>As <see cref="CountItems"/>, but across the whole pack — grid, crafting
    /// corners and equip row. What a conservation check wants: a move that shuffles an item
    /// out of the grid has not lost it.</summary>
    private static int CountEverywhere(Inventory inv, ItemKind kind)
    {
        int n = CountItems(inv, kind);
        foreach (var s in inv.Craft)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        foreach (var s in inv.Weapons)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        foreach (var s in inv.Break)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        foreach (var s in inv.Parts)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        return n;
    }

    private static string? GroundedShotMissesCore()
    {
        // A bolt at barrel height, dead-centre on the core's planar spot, must sail
        // underneath: only a leaping shot rides high enough to reach the gem.
        var boss = new Entities.CrabCore(Vector2.Zero);
        return boss.HitsCore(Vector2.Zero, Entities.Projectile.BoltHeight)
            ? "a grounded-height shot connected with the core"
            : null;
    }

    private static string? AirShotKillsCore()
    {
        var boss = new Entities.CrabCore(Vector2.Zero);
        if (!boss.HitsCore(Vector2.Zero, Entities.CrabCore.CoreHitHeight))
            return "a shot at core height missed the core";

        bool killedReported = false;
        for (int i = 0; i < 100 && boss.Alive; i++)
            killedReported = boss.DamageCore(1f);

        if (boss.Alive) return "core never depleted under repeated air hits";
        if (!killedReported) return "the killing hit didn't report the kill";

        // The death glitch should ramp as the rig tears apart, then finish (Dead).
        for (int i = 0; i < 60 * 3 && !boss.Dead; i++)
            boss.Update((float)Config.FixedDt, Vector2.Zero);
        if (!boss.Dead) return "death glitch never finished";
        return null;
    }

    private static string? AirShotExpiresForBlast()
    {
        // A shot launched well above barrel height is an air shot: it must glide out
        // and expire on its own (the flag the world reads to stage the horizon blast).
        var p = new Entities.Projectile();
        p.Fire(Vector2.Zero, new Vector2(0f, 1f), owner: 0, launchHeight: 6.5f);
        if (!p.IsAirShot) return "a high launch wasn't treated as an air shot";

        bool sawExpire = false;
        for (int i = 0; i < 60 * 6 && p.Active; i++)
        {
            p.Update((float)Config.FixedDt);
            if (p.JustExpired) sawExpire = true;
        }
        return sawExpire ? null : "air shot never expired to stage its blast";
    }

    private static string? DebugSpawnAddsEnemy()
    {
        // The in-game 'L' hatch: each call must put one more threat on the field —
        // a hunter or the Crab-Core. Spawning off so the director doesn't muddy the
        // count. Repeat enough to exercise every branch (both tanks and the boss).
        var world = new World.World { DynamicSpawning = false };
        for (int i = 0; i < 40; i++)
        {
            int tanks = world.Enemies.Count;
            bool hadBoss = world.Boss != null;
            world.SpawnRandomEnemy();

            bool grew = world.Enemies.Count > tanks || (!hadBoss && world.Boss != null);
            // At the hunter cap a spawn can swap rather than grow the count; only
            // count that as a miss if the boss slot didn't fill either.
            if (!grew && world.Enemies.Count < 1 && world.Boss == null)
                return "a debug spawn added no enemy";
        }
        return world.Enemies.Count > 0 ? null : "no hunters on the field after 40 spawns";
    }

    private static string? BossSeizesPlayer()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        if (!Entities.CrabSeizure.CanSeize(boss, player))
            return "a boss standing on the player wouldn't seize";

        // Run the whole cinematic and count the moments that cost the player.
        var seizure = new Entities.CrabSeizure(boss, player);
        int struck = 0, landed = 0;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            switch (seizure.Update((float)Config.FixedDt))
            {
                case Entities.CrabSeizure.Event.Struck: struck++; break;
                case Entities.CrabSeizure.Event.Landed: landed++; break;
            }
        }

        if (seizure.Active) return "the seizure never finished";
        // Each damage moment has to fire exactly once: a strike that repeated every
        // tick of the swing would delete the player outright.
        if (struck != 1) return $"the claw's blow fired {struck} times, expected 1";
        if (landed != 1) return $"the landing fired {landed} times, expected 1";

        // The player must be handed back: on the grid, driving again, and thrown
        // clear of the boss rather than dropped back inside its reach.
        if (player.Captured) return "the player was never released from the grip";
        if (player.Height > 0.001f) return "the player never came back down";
        float thrown = Vector2.Distance(player.Position, boss.Position);
        if (thrown <= Entities.CrabSeizure.GrabRadius)
            return $"the throw only moved the player {thrown:F1} units — still in reach";
        if (boss.Seizing) return "the boss is still posed as holding someone";
        return null;
    }

    /// <summary>
    /// The two hands have opposite jobs, and both are easy to get silently wrong.
    ///
    /// The striking one has to converge on the craft or the blow lands in empty air.
    /// The holding one has to stay off the line of sight: it is drawn from a walking
    /// leg, and the pose that raises it into an arm lifts the whole limb — knee
    /// included — so on the centre line it becomes a column through the middle of the
    /// shot and the player spends the scream looking at a leg instead of the crystal.
    ///
    /// Neither is visible from the numbers alone, because a claw's world position and
    /// the point the cinematic parks the craft at are reached down entirely separate
    /// paths: a pose yaw through the renderer's rotation convention, versus a forward
    /// offset through the seizure's.
    /// </summary>
    private static string? SeizureHandsReachThePlayer()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        var legs = Entities.CrabRig.Legs;
        var grab = legs[Entities.CrabRig.GrabLeg];
        var strike = legs[Entities.CrabRig.StrikeLeg];

        var seizure = new Entities.CrabSeizure(boss, player);
        bool sawHold = false;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            seizure.Update((float)Config.FixedDt);

            // Checked once the hold has settled: the drag in is an interpolation from
            // wherever the craft was standing, so only the stages after it are claimed
            // to have the player actually in the grip.
            if (seizure.Phase is not (Entities.CrabSeizure.Stage.Scream
                                   or Entities.CrabSeizure.Stage.Strike)) continue;
            sawHold = true;

            // The striking hand converges on the craft: it has to actually connect.
            // Generous, because the grip trembles and the blow knocks the craft off the
            // claw on purpose — this only catches a hand in the wrong place entirely.
            Vector2 hit = Entities.CrabRig.TipWorldXZ(
                strike, Entities.CrabRig.CentreGripYaw(strike), boss.Position, boss.Heading);
            float miss = Vector2.Distance(hit, player.Position);
            if (miss > 5f)
                return $"the striking claw is {miss:F1} from the player it should hit";

            // The holding hand must NOT. It is a limb the size of a building and the
            // pose that raises it carries its knee higher still, so anywhere near the
            // line of sight it becomes a column straight through the middle of the shot
            // with the core behind it. Held off to the side it frames the view instead.
            Vector2 held = Entities.CrabRig.TipWorldXZ(
                grab, Entities.CrabRig.HoldingGripYaw(grab), boss.Position, boss.Heading);
            Vector2 toClaw = held - player.Position;
            Vector2 view = boss.Position - player.Position;
            if (toClaw.LengthSquared() > 0.01f && view.LengthSquared() > 0.01f)
            {
                float off = MathF.Acos(Math.Clamp(Vector2.Dot(
                    Vector2.Normalize(toClaw), Vector2.Normalize(view)), -1f, 1f));
                if (off < 0.6f)
                    return $"the holding claw is only {off:F2} rad off the view axis "
                         + "— it will stand between the player and the core";
            }

            float lift = MathF.Abs(player.Height - Entities.CrabRig.HoldWorldY);
            if (lift > 3f)
                return $"the craft is carried {lift:F1} off the height it is held at";

            // The point of the whole arrangement: the holding claw grips from below the
            // eye. Level with it, the limb lies along the line of sight and the player
            // spends the scream looking at a leg instead of at the crystal.
            float eye = player.Height + Config.CameraHeight;
            if (Entities.CrabRig.GripWorldY >= eye - 1f)
                return $"the holding claw at {Entities.CrabRig.GripWorldY:F1} is not clear "
                     + $"below the eye at {eye:F1} — it will block the core";
        }
        return sawHold ? null : "the hold never played";
    }

    private static string? SeizureFramesTheCore()
    {
        // The whole point of being held is watching the core. The craft has to be
        // lifted so the eye sits inside the pyramid's vertical span — too low and the
        // player spends the scream staring at the chassis with the gem off-screen.
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        float gemBase = Entities.CrabRig.CoreWorldY;
        float gemApex = gemBase + Entities.CrabRig.CoreMeshHeight;

        var seizure = new Entities.CrabSeizure(boss, player);
        bool sawScream = false;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            seizure.Update((float)Config.FixedDt);
            if (seizure.Phase != Entities.CrabSeizure.Stage.Scream) continue;

            sawScream = true;
            float eye = player.Height + Config.CameraHeight;
            if (eye < gemBase || eye > gemApex)
                return $"eye at {eye:F1} is outside the core's {gemBase:F1}..{gemApex:F1} band";

            // And the camera has to actually be pointed at the gem. Checking the sign
            // of the seizure's own pitch is not the same thing and quietly passes while
            // the crystal sits off the bottom of the screen: the eye carries a standing
            // upward tilt of its own, so what matters is where the two together land at
            // the boss's distance, not whether the cinematic's share of it is positive.
            float slope = Config.CameraLookLift + seizure.Pitch;
            float aim = eye + slope * Vector2.Distance(player.Position, boss.Position);
            if (aim < gemBase || aim > gemApex)
                return $"the view is aimed at {aim:F1}, outside the core's "
                     + $"{gemBase:F1}..{gemApex:F1} band";
        }
        return sawScream ? null : "the scream stage never played";
    }

    /// <summary>
    /// The lance's one promise: once it fires, it fires where the player <em>was</em>.
    ///
    /// This is the property the whole attack is balanced on — the charge is a window
    /// to leave the line, and that window is only real if walking out of it works. So
    /// this drives a boss all the way to the shot and then teleports the player a long
    /// way sideways mid-burn, and asserts the beam neither turns to follow nor lands a
    /// hit. If a future change ever makes the beam track, this fails rather than the
    /// attack quietly becoming unavoidable.
    /// </summary>
    private static string? BeamLocksItsDirection()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "boss never reached pursuit";

        // Stand it off at lance range — inside the grab radius it goes for the claw.
        var aimedAt = new Vector2(0f, 34f);
        boss.Position = aimedAt;
        float dt = (float)Config.FixedDt;

        // The cooldown runs down over several seconds of pursuit, during which the
        // boss is walking in. Pin it at range each tick — in play that gap is held by
        // the player outrunning it, which is the situation the attack exists for; here
        // it just keeps the wait from ending with the crab in the player's lap.
        for (int i = 0; i < 60 * 30 && !boss.BeamActive; i++)
        {
            if (boss.Phase == Entities.CrabCore.State.Pursuit) boss.Position = aimedAt;
            boss.Update(dt, player.Position);
        }

        if (!boss.BeamActive) return "boss never fired its beam in 30s of pursuit";

        Vector3 firedAlong = boss.BeamDirection;

        // First: it is aimed at the player it locked. This is what pins the bearing
        // and elevation conventions together — get either of them mirrored and the
        // beam still fires, still holds its line, and still misses every time.
        if (!InBeam(boss, player.Position, firedAlong))
            return "the beam did not point at the player it locked onto";

        // Now break for cover: straight out to one side, well clear of the shaft.
        player.Position = new Vector2(60f, 0f);

        for (int i = 0; i < 60 * 4 && boss.BeamActive; i++)
        {
            boss.Update(dt, player.Position);
            if (!boss.BeamActive) break;

            if (Vector3.Distance(boss.BeamDirection, firedAlong) > 0.001f)
                return "the beam turned to follow the player after firing";

            // ...and the player who ran is genuinely out of it.
            if (InBeam(boss, player.Position, firedAlong))
                return "a player who ran clear was still inside the beam";
        }

        return null;
    }

    /// <summary>Whether a craft standing at <paramref name="at"/> is inside the
    /// boss's beam — the same point-to-ray test the world damages on.</summary>
    private static bool InBeam(Entities.CrabCore boss, Vector2 at, Vector3 dir)
    {
        var p = new Vector3(at.X, 1f, at.Y);
        Vector3 from = boss.BeamOrigin;
        float along = Math.Clamp(Vector3.Dot(p - from, dir), 0f, Entities.CrabCore.BeamLength);
        return Vector3.Distance(p, from + dir * along) <= Entities.CrabCore.BeamRadius;
    }

    /// <summary>
    /// Builds a boss and a player standing in each other's laps and runs the Stalker
    /// Protocol forward until it commits to the hunt — the state a seizure needs.
    /// Returns nulls if it never got there.
    /// </summary>
    private static (Entities.CrabCore?, Entities.PlayerTank?) CorneredByBoss()
    {
        var player = new Entities.PlayerTank(Vector2.Zero);
        var boss = new Entities.CrabCore(new Vector2(0f, 9f));

        // Idle -> threat display -> clamping -> pursuit takes a few fixed seconds.
        for (int i = 0; i < 60 * 10 && boss.Phase != Entities.CrabCore.State.Pursuit; i++)
            boss.Update((float)Config.FixedDt, player.Position);

        if (boss.Phase != Entities.CrabCore.State.Pursuit) return (null, null);

        // The display slides it sideways, so walk it back into arm's reach.
        boss.Position = player.Position + new Vector2(0f, 9f);
        boss.SnapToFace(player.Position);
        return (boss, player);
    }

    // --- The Maw-Core: the hanging mouth --------------------------------------

    private static string? MawHangsAtJumpApex()
    {
        // The load-bearing claim of the whole enemy: its crystal sits where a bolt
        // fired at the peak of a leap is travelling. Checked against the jump's own
        // physics rather than against a copy of the number, so retuning the jump
        // without moving the monster fails here rather than silently in play.
        float apexShot = Entities.PlayerTank.JumpApex + Entities.Projectile.BoltHeight;
        var maw = new Entities.MawCore(Vector2.Zero);

        if (!maw.HitsCrystal(Vector2.Zero, apexShot))
            return $"a shot at apex height {apexShot:F2} missed the crystal";

        // The band must also be tight enough that it is genuinely a jump check: a shot
        // from halfway up the arc has to miss, or "shoot it while airborne" collapses
        // into "shoot it while vaguely off the ground".
        float halfway = Entities.PlayerTank.JumpApex * 0.5f + Entities.Projectile.BoltHeight;
        return maw.HitsCrystal(Vector2.Zero, halfway)
            ? $"a shot from halfway up the jump ({halfway:F2}) still reached the crystal"
            : null;
    }

    private static string? MawNeedsAnAirShot()
    {
        var maw = new Entities.MawCore(Vector2.Zero);

        // Grounded: must sail underneath, however well aimed.
        if (maw.HitsCrystal(Vector2.Zero, Entities.Projectile.BoltHeight))
            return "a grounded-height shot connected with the crystal";

        // ...and the crystal must actually be destructible from the air, glitch and all.
        if (!maw.HitsCrystal(Vector2.Zero, Entities.MawRig.CrystalWorldY))
            return "a shot at the strike band missed the crystal";

        bool killedReported = false;
        for (int i = 0; i < 100 && maw.Alive; i++)
            killedReported = maw.DamageCrystal(1f);

        if (maw.Alive) return "crystal never depleted under repeated air hits";
        if (!killedReported) return "the killing hit didn't report the kill";

        for (int i = 0; i < 60 * 3 && !maw.Dead; i++)
            maw.Update((float)Config.FixedDt, Vector2.Zero, 0f);
        return maw.Dead ? null : "death glitch never finished";
    }

    private static string? MawSwallowsStillPlayer()
    {
        // Standing still under it has to end in being caught — that is the deal, and
        // UnderTheMaw only returns a pair once JustCaught has actually fired.
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never dropped on a still player";
        if (maw.Phase != Entities.MawCore.State.Digest)
            return $"the maw caught the player but sat in {maw.Phase}";

        // ...and the other half of the deal: a player who keeps walking is never
        // caught, so the lunge has to miss someone who left the column.
        var mover = new Entities.PlayerTank(Vector2.Zero);
        var missing = new Entities.MawCore(Vector2.Zero);
        for (int i = 0; i < 60 * 10; i++)
        {
            // Walking flat out, straight line — the simplest possible evasion.
            mover.Position += new Vector2(0f, Entities.PlayerTank.MaxSpeed * (float)Config.FixedDt);
            missing.Update((float)Config.FixedDt, mover.Position, mover.Height);
            if (missing.JustCaught) return "the maw caught a player who never stopped moving";
        }
        return null;
    }

    private static string? MawReleasesOnThreeShots()
    {
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never caught the player";

        var digestion = new Entities.MawDigestion(maw, player);

        // Step into the hold, then put the escape shots in. Fewer than three must not
        // free anybody — that is the whole tension of the beat.
        for (int i = 0; i < 60 * 2 && digestion.Phase != Entities.MawDigestion.Stage.Digest; i++)
            digestion.Update((float)Config.FixedDt);
        if (digestion.Phase != Entities.MawDigestion.Stage.Digest)
            return "the swallow never reached the digest stage";

        if (digestion.RegisterShot()) return "one shot freed the player";
        if (digestion.RegisterShot()) return "two shots freed the player";
        if (!digestion.RegisterShot()) return "three shots did not free the player";
        if (digestion.Hits != Entities.MawDigestion.EscapeHits)
            return $"escape counted {digestion.Hits} hits, expected {Entities.MawDigestion.EscapeHits}";

        // The whole cinematic must then play out and hand control back — a trap the
        // player can never drive out of is a hang, not a set piece.
        bool landed = false;
        for (int i = 0; i < 60 * 12 && digestion.Active; i++)
            if (digestion.Update((float)Config.FixedDt) == Entities.MawDigestion.Event.Landed)
                landed = true;

        if (digestion.Active) return "the digestion never finished";
        if (!landed) return "the player was never put back on the grid";
        if (player.Captured) return "control was never handed back";
        return null;
    }

    private static string? MawDigestionBites()
    {
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never caught the player";

        var digestion = new Entities.MawDigestion(maw, player);

        // Held and never shooting back: the bites have to keep coming. Run long enough
        // to see several, so a single one firing on entry wouldn't pass this.
        int bites = 0;
        for (int i = 0; i < 60 * 6 && digestion.Held; i++)
            if (digestion.Update((float)Config.FixedDt) == Entities.MawDigestion.Event.Bitten)
                bites++;

        if (bites < 3) return $"only {bites} bite(s) in six seconds of being digested";

        // And each one must be worth 15% of a shield, which is what makes the escape
        // urgent rather than optional.
        if (MathF.Abs(Entities.MawDigestion.BiteFraction - 0.15f) > 0.001f)
            return $"a bite costs {Entities.MawDigestion.BiteFraction:P0}, expected 15%";
        return null;
    }

    private static string? MawEscapeThroughTheGun()
    {
        // The escape driven the way a player actually drives it: through the world's
        // own fire entry point, against a live World, with the cannon's real cooldown
        // and ammo in the way.
        //
        // This exists because testing the digestion in isolation is not enough and once
        // shipped a trap with no exit. MawReleasesOnThreeShots calls RegisterShot()
        // directly, which sails straight past PlayerTank.TryFire — and TryFire depends
        // on a cooldown that Update() used to skip entirely while Captured. The player
        // got one shot, then the gun stayed locked for the whole hold and the only way
        // out of the monster was sealed. Every layer between the button and the effect
        // has to be in the loop or the check proves nothing.
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();

        var player = world.Player;
        var maw = new Entities.MawCore(player.Position);
        world.AttachMawForTest(maw);

        // Stand still and be caught.
        for (int i = 0; i < 60 * 10 && world.Digestion == null; i++)
            StepWithoutInput(world);
        if (world.Digestion == null) return "the maw never swallowed a stationary player";

        int ammo0 = player.Ammo;

        // Hold the trigger down, exactly as a panicking player would, and step the sim.
        // Three shots at a 0.35s cooldown is about a second of being chewed.
        for (int i = 0; i < 60 * 8 && world.Digestion is { Held: true }; i++)
        {
            world.FirePlayerShot();
            StepWithoutInput(world);
        }

        if (world.Digestion is { Held: true })
            return "holding fire for eight seconds never broke the maw's hold";
        if (player.Ammo >= ammo0)
            return "the escape shots never cost any ammo";
        if (ammo0 - player.Ammo > 8)
            return $"the escape burned {ammo0 - player.Ammo} rounds — the cooldown isn't holding";

        // ...and the player ends up back on the grid, driving.
        for (int i = 0; i < 60 * 12 && world.Digestion != null; i++)
            StepWithoutInput(world);
        if (world.Digestion != null) return "the digestion never finished";
        if (player.Captured) return "control was never handed back";
        return null;
    }

    /// <summary>
    /// Hangs a maw directly over a stationary player and runs it forward until its
    /// lunge closes over them — the state a digestion needs. Returns nulls if it never
    /// got there, which is itself the failure worth reporting.
    /// </summary>
    private static (Entities.MawCore?, Entities.PlayerTank?) UnderTheMaw()
    {
        var player = new Entities.PlayerTank(Vector2.Zero);
        var maw = new Entities.MawCore(Vector2.Zero);

        // Hover -> (windup) -> lunge -> caught. The player never moves, which is the
        // one behaviour this monster punishes.
        for (int i = 0; i < 60 * 10; i++)
        {
            maw.Update((float)Config.FixedDt, player.Position, player.Height);
            if (maw.JustCaught) return (maw, player);
        }
        return (null, null);
    }

    // --- The SOLDIER -----------------------------------------------------------

    /// <summary>
    /// Every other chassis opens at the origin, which the skyline is deliberately kept
    /// out of. That start is useless to this one: its whole loop is anchors, and there
    /// is nothing to anchor to inside the clearing. So the check is not "does it stand
    /// somewhere sensible" but the thing the player actually experiences — on frame one,
    /// with nothing touched, is there something the crosshair can bite?
    /// </summary>
    private static string? SoldierStartsAtAnAnchor()
    {
        var world = SoldierWorld();
        var p = world.Player;

        if (p.Soldier == null) return "the soldier loadout produced a craft with no rig";

        float toCity = float.MaxValue;
        foreach (var s in world.Structures)
            toCity = MathF.Min(toCity, Torus.Distance(s.Position, p.Position));
        if (toCity > Entities.SoldierRig.MaxRange)
            return $"opens {toCity:0} units from the nearest building — past a cable's {Entities.SoldierRig.MaxRange:0}";

        if (!world.TryFindAnchor(p.Eye, p.Forward3, out Vector3 at, out _))
            return "the opening view has no anchor in it at all";

        float range = Vector3.Distance(p.Eye, at);
        if (range > Entities.SoldierRig.MaxRange)
            return $"the anchor in sight is {range:0} out, past the rig's reach";
        return null;
    }

    /// <summary>
    /// The opener. It has to clear enough to matter (the spec asks for 12 to 18 metres),
    /// take about 1.2 seconds getting there, and cost a visible chunk of the bottle —
    /// a jump that were free would make the reserve meaningless.
    /// </summary>
    private static string? SoldierJumpClearsTheCity()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        float gas0 = p.Hyper;
        if (!rig.Jump(p)) return "a standing soldier refused to jump on a full reserve";
        if (p.Hyper >= gas0) return "the jump cost no gas";

        float peak = 0f;
        float apexAt = 0f;
        int nearPeak = 0;
        for (int i = 0; i < 60 * 6; i++)
        {
            StepWithoutInput(world);
            if (p.Height > peak) { peak = p.Height; apexAt = (i + 1) / 60f; }
            // Frames spent within a metre of the top: the hang, counted.
            if (peak > 0f && p.Height > peak - 1f) nearPeak++;
            if (peak > 0f && p.Height <= 0f) break;
        }

        if (peak < 12f || peak > 18f) return $"the jump peaked at {peak:0.0}m, wanted 12-18";
        if (apexAt < 1.05f || apexAt > 1.4f) return $"took {apexAt:0.00}s to the apex, wanted ~1.2";

        // And the floaty hang at the top has to actually be there. Without it the apex
        // is a corner rather than a beat, and the beat is where the player picks the
        // anchor they are about to fire at.
        float hang = nearPeak / 60f;
        if (hang < 0.35f) return $"only {hang:0.00}s spent at the top — there is no hang";

        if (p.Height > 0.01f) return "the soldier never came back down";
        return null;
    }

    /// <summary>
    /// The core of the class: aim at a building, press the key, and a second later be
    /// hanging off it. Drives it exactly the way the player does — through the world's
    /// own fire path, against a live raycast, stepping the sim between.
    /// </summary>
    private static string? SoldierHookBites()
    {
        var world = SoldierWorld();
        var rig = world.Player.Soldier!;

        if (!world.FireSoldierHookForTest(right: true))
            return "nothing in the opening view to fire at";
        if (rig.Right.State != Entities.HookState.Flying)
            return "the hook never left the launcher";

        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);

        if (!rig.Right.Anchored) return "the hook flew but never bit anything";
        if (rig.Right.Holding == null) return "the hook bit, but is holding nothing";

        // And what it bit has to be where it was drawn to bite: on the surface of the
        // thing, not floating in the air next to it or buried in its middle.
        Structure s = rig.Right.Holding!;
        float off = Torus.Distance(rig.Right.Tip, s.Position);
        if (off > 12f) return $"the bite landed {off:0.0} units from the building it claims";
        if (rig.Right.TipY < 0f) return "the bite landed underground";
        return null;
    }

    /// <summary>
    /// A cable is a hard constraint, not a rope that stretches: the player may never end
    /// up further from the anchor than its length, and gravity acting on a body held at
    /// a fixed radius has to produce a <em>swing</em> — lateral travel — rather than a
    /// fall. Both halves matter. A constraint that held but killed all momentum would
    /// pass the first and make the class unplayable.
    /// </summary>
    private static string? SoldierCableSwings()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        // Up into the air first, so there is somewhere to swing to.
        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);

        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        float length = rig.Right.Length;
        float worstOver = 0f;
        float travelled = 0f;
        Vector2 was = p.Position;

        for (int i = 0; i < 60 * 4; i++)
        {
            StepWithoutInput(world);
            if (!rig.Right.Anchored) break;

            var to = new Vector3(
                Torus.Delta(p.Position, rig.Right.Tip).X,
                rig.Right.TipY - p.Height - Entities.SoldierRig.ShoulderHeight,
                Torus.Delta(p.Position, rig.Right.Tip).Y);
            worstOver = MathF.Max(worstOver, to.Length() - rig.Right.Length);

            travelled += Torus.Distance(was, p.Position);
            was = p.Position;
        }

        // A centimetre of overshoot inside one step is the solver settling; a metre is
        // a rope made of elastic.
        if (worstOver > 0.25f)
            return $"the cable stretched {worstOver:0.00} past its length";
        if (travelled < length * 0.5f)
            return $"hanging on a {length:0}m cable only moved the player {travelled:0.0}m — it is not swinging";
        return null;
    }

    /// <summary>
    /// The slingshot. Letting go has to do <em>nothing</em> — no impulse, no damping,
    /// no snap — because everything the player built in the arc is theirs and the whole
    /// expressive ceiling of the class is choosing the moment to stop being attached.
    /// </summary>
    private static string? SoldierReleaseKeepsMomentum()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        // Swing until there is real speed on the clock.
        for (int i = 0; i < 60 * 3 && rig.PlanarSpeed < 8f; i++) StepWithoutInput(world);
        if (rig.PlanarSpeed < 8f) return "the swing never built any speed to keep";

        Vector3 before = rig.Velocity;
        rig.ReleaseHook(right: true);
        if (rig.Velocity != before)
            return "letting go of the cable changed the player's momentum";

        // And one step later it should differ only by gravity — nothing else may touch it.
        StepWithoutInput(world);
        var planarBefore = new Vector2(before.X, before.Z);
        var planarAfter = new Vector2(rig.Velocity.X, rig.Velocity.Z);
        if (Vector2.Distance(planarBefore, planarAfter) > 0.5f)
            return "the released player's planar momentum was damped";
        return null;
    }

    /// <summary>
    /// Reeling is the engine: it has to convert gas into speed. If it costs nothing the
    /// reserve is decoration, and if it produces nothing the cables are a tether rather
    /// than a way to travel.
    /// </summary>
    private static string? SoldierReelBurnsGas()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        float gas0 = p.Hyper;
        float length0 = rig.Right.Length;
        float speed0 = rig.Speed;

        // Hold W for a second, which is what a player crossing a gap does.
        rig.MoveInput = new Vector2(0f, 1f);
        for (int i = 0; i < 60; i++) StepWithoutInput(world);
        rig.MoveInput = Vector2.Zero;

        if (p.Hyper >= gas0) return "a second of reeling cost no gas";
        if (rig.Right.Length >= length0) return "reeling didn't shorten the cable";
        if (rig.Speed <= speed0) return "reeling didn't accelerate the player";
        if (!rig.Right.Anchored) return "reeling shook the hook loose";
        return null;
    }

    /// <summary>
    /// The single most important property of a mouse-aimed weapon: the round goes where
    /// the crosshair is. On every other chassis the gun points where the chassis points
    /// and there is nothing to get wrong; here the aim, the eye, the muzzle offset and
    /// the round's own climb are four separate pieces of arithmetic, and any one of them
    /// being off by a few degrees is invisible standing still and infuriating in a fight.
    ///
    /// Checked at several pitches, including steep ones, because the failure this is
    /// really guarding against — treating a look <em>slope</em> as a look <em>angle</em>
    /// — is nearly exact at level and badly wrong the moment the player looks up.
    /// </summary>
    private static string? SoldierRifleFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = SoldierWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // Fired from the air, which is where this chassis actually shoots from — and
            // is also the only way a steeply downward shot has any flight to measure
            // before it correctly buries itself in the grid a metre and a half below.
            p.Height = 40f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireSoldierRifleForTest();
            // Two steps, so the round has genuinely travelled and any per-step error has
            // had a chance to accumulate rather than hiding in the launch offset.
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the air at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            // How far off the line of sight it is, as an angle — which is the number a
            // player actually experiences, and the one that stays meaningful whatever
            // the range happens to be.
            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the round flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>
    /// A rocket takes the building down, and anything hanging from it goes with it. This
    /// is the one rule that makes the rockets frightening rather than free: the player is
    /// entirely capable of shooting away the thing holding them up.
    /// </summary>
    private static string? SoldierAnchorDiesWithItsTower()
    {
        var world = SoldierWorld();
        var rig = world.Player.Soldier!;

        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        Structure held = rig.Right.Holding!;
        held.Strike();   // whatever cut it down — a rocket, a lance, the sky falling

        for (int i = 0; i < 60 && rig.Right.Anchored; i++) StepWithoutInput(world);

        if (rig.Right.Anchored)
            return "the player is still hanging from a building that is on its way down";
        if (rig.Right.Holding != null)
            return "the torn hook is still holding a reference to the wreck";
        return null;
    }

    /// <summary>
    /// Weak material gives way. The point is not the failure itself but that it is
    /// <em>readable</em>: the same scale threshold the HUD warns on is the one that
    /// tears, so a player who learns to distrust thin spires is learning something true.
    /// </summary>
    private static string? SoldierWeakAnchorTears()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        Structure held = rig.Right.Holding!;
        bool weak = held.Scale < Entities.SoldierRig.WeakScale;

        // Hang on it for comfortably longer than weak material is supposed to hold.
        for (int i = 0; i < (int)(60 * (Entities.SoldierRig.TearTime + 1.5f)); i++)
        {
            StepWithoutInput(world);
            if (!rig.Right.Anchored) break;
        }

        if (weak && rig.Right.Anchored)
            return $"a scale-{held.Scale:0.00} anchor held for good — weak material never tears";
        if (!weak && !rig.Right.Anchored)
            return $"a scale-{held.Scale:0.00} anchor let go — solid material is failing";
        return null;
    }

    /// <summary>A stage with a soldier in it and nothing else moving: no spawn director,
    /// no hunters, so the checks above are measuring the rig and not a firefight.</summary>
    private static World.World SoldierWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Soldier };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    // --- The enemy SOLDIER squads ------------------------------------------------

    /// <summary>
    /// The arrival. Four of them, exactly one wearing the leader's mark, and — the part
    /// that actually matters — already hanging off the side of a building rather than
    /// standing on the grid or floating in mid-air. The first thing a player ever sees of a
    /// squad is four figures on a spire, and that only happens if the spawn genuinely finds
    /// one and pins them to it.
    /// </summary>
    private static string? SquadArrivesOnATower()
    {
        var world = SquadWorld();

        int perched = 0;
        for (int i = 0; i < 4; i++)
        {
            world.Soldiers.Clear();
            world.Squads.Clear();
            world.SpawnSoldierSquad();

            if (world.Soldiers.Count != Entities.SoldierSquad.Size)
                return $"a squad arrived {world.Soldiers.Count} strong, wanted {Entities.SoldierSquad.Size}";

            int leaders = 0;
            foreach (var s in world.Soldiers) if (s.IsLeader) leaders++;
            if (leaders != 1) return $"{leaders} of the four wore the leader's mark";

            bool up = true;
            foreach (var s in world.Soldiers)
            {
                if (s.Move != Entities.SoldierMove.Perched) { up = false; break; }
                if (s.Height < 5f) return $"a perched soldier opened {s.Height:0.0}m up — that is the floor";
                if (!s.Right.Anchored) return "a perched soldier is holding on to nothing";
                if (s.PerchedOn == null) return "a perched soldier is clung to no building";
            }
            if (up) perched++;
        }

        // A bearing that lands on open grid is a real outcome and the squad walks in from
        // there; a city this dense should not produce four of them in a row.
        return perched > 0 ? null : "four squads in a row found nothing to perch on";
    }

    /// <summary>
    /// Seeing the player and doing something about it. The call goes out exactly once — a
    /// squad that re-alerted every tick would be a klaxon — and then the distance has to
    /// actually close, which is the whole test: it is one thing to write a flier and quite
    /// another for it to arrive.
    /// </summary>
    private static string? SquadCallsAndCloses()
    {
        var world = SquadWorld();
        world.SpawnSoldierSquad();
        var squad = world.Squads[0];

        float opening = NearestSoldier(world);
        int calls = 0;

        for (int i = 0; i < 60 * 30; i++)
        {
            StepWithoutInput(world);
            if (squad.JustCalled) calls++;
            if (world.Soldiers.Count == 0) break;
            if (NearestSoldier(world) < 45f) break;
        }

        if (calls != 1) return $"the squad called {calls} times, wanted exactly one";
        if (!squad.Alerted) return "the squad never noticed the player at all";

        float closed = NearestSoldier(world);
        if (closed > 45f)
            return $"thirty seconds on, the nearest of them is still {closed:0} out (opened at {opening:0})";
        return null;
    }

    /// <summary>
    /// The claim the whole enemy rests on: they get around on the cables. Their jets are
    /// deliberately feeble — enough to bend a line, nowhere near enough to fly — so any
    /// speed appreciably past what a jet alone could ever produce is proof that a taut
    /// steel line put it there. If this fails, what is on screen is a drone with legs.
    /// </summary>
    private static string? SoldiersFlyOnTheirCables()
    {
        var world = SquadWorld();
        world.SpawnSoldierSquad();

        float fastest = 0f;
        int anchoredTicks = 0;
        int airTicks = 0;

        for (int i = 0; i < 60 * 25 && world.Soldiers.Count > 0; i++)
        {
            StepWithoutInput(world);
            foreach (var s in world.Soldiers)
            {
                fastest = MathF.Max(fastest, s.PlanarSpeed);
                if (s.AnyAnchored) anchoredTicks++;
                if (s.Height > 2f) airTicks++;
            }
        }

        if (airTicks == 0) return "nobody in the squad left the ground";
        if (anchoredTicks == 0) return "nobody in the squad ever put a hook into anything";
        if (fastest < 16f)
            return $"the fastest of them managed {fastest:0.0} m/s — the jets alone would do that";
        return null;
    }

    /// <summary>
    /// The squad's whole contribution: exactly one of them is committed at a time, so the
    /// fight is a rhythm the player can read rather than four knives at once. And the turn
    /// has to actually go round — a rota that hands every run to the same soldier is not a
    /// rota, it is one enemy and three spectators.
    /// </summary>
    private static string? SquadStrikesInTurn()
    {
        var world = SquadWorld();
        world.SpawnSoldierSquad();
        var squad = world.Squads[0];

        int worst = 0;
        var tookATurn = new HashSet<int>();
        int windows = 0;
        Entities.EnemySoldier? last = null;

        for (int i = 0; i < 60 * 45 && world.Soldiers.Count > 0; i++)
        {
            StepWithoutInput(world);

            int out_ = 0;
            foreach (var s in world.Soldiers) if (s.BladesOut) out_++;
            worst = Math.Max(worst, out_);

            if (squad.Striker is { } who)
            {
                tookATurn.Add(who.Slot);
                if (!ReferenceEquals(who, last)) windows++;
                last = who;
            }
            else last = null;
        }

        if (worst > 1) return $"{worst} of them had blades out at once — the rota is not holding";
        if (windows == 0) return "nobody was ever sent in across forty-five seconds";
        if (world.Soldiers.Count > 1 && tookATurn.Count < 2)
            return "every run went to the same soldier — the turn never passed";
        return null;
    }

    /// <summary>
    /// Where they live is the whole defence. A soldier thirty metres up a tower has to be
    /// missed by the flat round every gun in this game fires along the grid, and hit by one
    /// that was actually aimed at them — which, for the player, means looking up. Both
    /// halves are checked against the same target from the same spot, so the only thing that
    /// differs between the pass and the fail is where the crosshair was pointing.
    /// </summary>
    private static string? SoldiersAreHitAtTheirOwnHeight()
    {
        var world = SoldierWorld();
        var p = world.Player;

        // Thirty metres out along the opening view, twelve metres up.
        const float Out = 30f, Up = 12f;
        Vector2 at = Torus.Wrap(p.Position + p.Forward * Out);
        var mark = new Entities.EnemySoldier(at, Up, leader: false, slot: 0);
        mark.SeedPerch(at, Up + 3f, null, p.Heading + MathF.PI);
        world.Soldiers.Add(mark);

        // Level first: a flat round down the barrel line passes a long way under them.
        p.Pitch = 0f;
        for (int i = 0; i < 60 * 2; i++)
        {
            world.FireSoldierRifleForTest();
            StepWithoutInput(world);
        }
        if (mark.Shield < Entities.EnemySoldier.BaseShield)
            return "a level round reached somebody twelve metres overhead";

        // Now aimed at their chest. Solved rather than guessed, so the check is about the
        // hit test and not about whether the number was typed in correctly.
        float rise = Up + Entities.EnemySoldier.AimHeight - p.Eye.Y;
        p.Pitch = MathF.Atan2(rise, Out);
        for (int i = 0; i < 60 * 3 && mark.Alive; i++)
        {
            world.FireSoldierRifleForTest();
            StepWithoutInput(world);
        }

        return mark.Alive
            ? $"three seconds of aimed fire left them on {mark.Shield:0.0} shield"
            : null;
    }

    /// <summary>A body dropped out of the air still pays out, and it pays out where it
    /// fell — a kill you earned by tracking one across a skyline is worth doubling back
    /// through.</summary>
    private static string? SoldierKillLeavesSalvage()
    {
        var world = SoldierWorld();
        var mark = new Entities.EnemySoldier(world.Player.Position + new Vector2(0f, 12f),
            8f, leader: false, slot: 0);
        world.Soldiers.Add(mark);

        int before = world.Pickups.Count;
        // Straight through the world's own damage path — a rocket detonated on them.
        world.Player.Pitch = MathF.Atan2(8f + 1f - world.Player.Eye.Y, 12f);
        world.Player.Heading = 0f;
        for (int i = 0; i < 60 * 2 && mark.Alive; i++)
        {
            world.FireSoldierRifleForTest();
            StepWithoutInput(world);
        }

        if (mark.Alive) return "the target survived two seconds of point-blank fire";
        if (world.Pickups.Count <= before) return "the body left nothing behind";
        return null;
    }

    /// <summary>
    /// The blades. A pass that arrives at the player's body, level with it and still
    /// carrying the arc that got it there, has to cost real shield — and having landed one,
    /// that soldier has to break off rather than grind away on the spot, which is what keeps
    /// four of them from being a blender.
    /// </summary>
    private static string? SoldierBladesCut()
    {
        var world = SoldierWorld();
        var p = world.Player;
        p.Height = 6f;

        // Placed just outside the blades' reach, already travelling at the player fast
        // enough for the pass to count, and told to commit.
        Vector2 at = Torus.Wrap(p.Position + new Vector2(0f, 3f));
        var killer = new Entities.EnemySoldier(at, 6f, leader: false, slot: 0)
        {
            Velocity = new Vector3(0f, 0f, -18f),
        };
        killer.CommitRunForTest();
        world.Soldiers.Add(killer);

        float shield = p.Shield;
        for (int i = 0; i < 12 && p.Shield >= shield; i++) StepWithoutInput(world);

        if (p.Shield >= shield) return "a run straight through the player cost them nothing";
        if (killer.BladeReady) return "the blades landed but were not spent — they can cut again at once";
        if (killer.Move == Entities.SoldierMove.Diving)
            return "the soldier is still on the run it just finished";
        return null;
    }

    /// <summary>
    /// The one question a soldier asks the world, and the four properties of a good answer.
    /// This is where the flying comes from: an anchor behind them brakes, one at arm's
    /// length does nothing, one below them cannot be swung under, and one on the smallest
    /// quarter of the skyline tears out mid-arc. Get this wrong and the physics above it
    /// still works perfectly — it just looks like flailing.
    /// </summary>
    private static string? AnchorQueryReadsTheCity()
    {
        var world = SoldierWorld();
        var p = world.Player;

        var from = new Vector3(p.Position.X, 14f, p.Position.Y);
        var wish = new Vector3(p.Forward.X, 0.2f, p.Forward.Y);

        if (!world.TryFindSwing(from, wish, Entities.SoldierRig.MaxRange,
                out Vector3 point, out Structure? holding))
            return "the opening view of a city offered nothing to swing from";
        if (holding == null) return "an anchor was found that belongs to no building";

        Vector2 delta = new Vector2(point.X, point.Z) - new Vector2(from.X, from.Z);
        float range = delta.Length();
        if (range > Entities.SoldierRig.MaxRange)
            return $"the anchor is {range:0} out, past a cable's {Entities.SoldierRig.MaxRange:0}";
        if (point.Y <= from.Y)
            return "the anchor is level with or below the flier — that is a rope, not a swing";

        Vector2 wishXZ = Vector2.Normalize(new Vector2(wish.X, wish.Z));
        float ahead = Vector2.Dot(Vector2.Normalize(delta), wishXZ);
        if (ahead <= 0f) return $"the anchor sits behind the direction of travel (dot {ahead:0.00})";

        // And the answer has to genuinely follow the question, asked all the way round the
        // compass rather than only down the one bearing that was bound to work.
        //
        // Stated carefully, because the rule has a deliberate exception in it: a cable
        // behind you is a brake and picking one is the most recognisable mistake an amateur
        // makes — but a soldier falling through a gap in the skyline with nothing ahead of
        // them takes the brake and lives, so the query prefers rather than requires. What is
        // checked here is exactly that: whenever the city genuinely does have something
        // standing along the bearing, the answer has to be along the bearing.
        var fromXZ = new Vector2(from.X, from.Z);
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8f;
            var spin = new Vector3(MathF.Sin(a), 0.2f, MathF.Cos(a));
            var spinXZ = new Vector2(spin.X, spin.Z);

            // Is there anything out that way at all? Towers only, and only ones that reach
            // above the flier: an arch is eight metres of leg holding a span nothing can
            // bite, so a soldier fourteen metres up has no more use for one than for open
            // grid. Measured well inside the query's own band, so a building it would reject
            // for being too close or too far can't be mistaken for one it ignored.
            bool anythingAhead = false;
            foreach (var st in world.Structures)
            {
                if (st.Kind != StructureKind.Tower) continue;
                if (st.BlockHeight * 0.92f < from.Y + 4f) continue;
                // Solid stone only. A spire ahead losing to good stone a few degrees off the
                // bearing is the query working, not failing � weak material tears out from
                // under a swing, and reading the city for that is the skill being modelled.
                if (st.Scale < Entities.SoldierRig.WeakScale) continue;
                Vector2 d = Torus.Delta(fromXZ, st.Position);
                float len = d.Length();
                if (len < 24f || len > Entities.SoldierRig.MaxRange - 10f) continue;
                if (Vector2.Dot(d / len, spinXZ) <= 0.4f) continue;
                anythingAhead = true;
                break;
            }
            if (!anythingAhead) continue;

            if (!world.TryFindSwing(from, spin, Entities.SoldierRig.MaxRange,
                    out Vector3 got, out _))
                return $"a bearing with a building standing along it came back empty";

            Vector2 toIt = new Vector2(got.X, got.Z) - fromXZ;
            if (Vector2.Dot(Vector2.Normalize(toIt), spinXZ) <= 0.1f)
                return "there was solid stone along the bearing and it picked something behind";
        }

        return null;
    }

    /// <summary>
    /// Shoot the wall, not the man. A cable is only as good as what it is bitten into, so
    /// the building coming apart has to take the anchor with it — and, because towers come
    /// apart <em>where they are hit</em> rather than toppling whole, a cut that leaves the
    /// stump standing has to count too, or a hook goes on holding a chunk of wall that is
    /// already rubble on the floor.
    ///
    /// The other half is what they do about it, and it is the half that decides whether
    /// this is a way to kill one or merely a way to annoy one: the line has to be back out
    /// and into something else within a second or so. Driven against a one-tower city so
    /// the check is about the reflex and not about which spire they happened to pick.
    /// </summary>
    private static string? SoldierAnchorDiesWithItsWall()
    {
        var city = new TwoTowers();
        var soldier = new Entities.EnemySoldier(new Vector2(0f, -30f), 20f, leader: false, slot: 0)
        {
            Order = Entities.SoldierOrder.Press,
        };

        // Let it fly and get a line into the first tower.
        for (int i = 0; i < 60 * 4 && !soldier.AnyAnchored; i++)
            soldier.Update((float)Config.FixedDt, new Vector2(0f, 60f), 0f, city);
        if (!soldier.AnyAnchored) return "the soldier never anchored to anything at all";

        Structure held = soldier.Left.Anchored ? soldier.Left.Holding! : soldier.Right.Holding!;

        // Cut a hole in it, well away from the base, so it is emphatically still standing.
        // Hard enough that cells genuinely break — a scratch that leaves the shape intact is
        // not a cut and should not shake anybody off.
        var detached = new List<DebrisSpawn>();
        held.CutTower(new Vector3(held.Position.X, 30f, held.Position.Y), 7f, 90f, detached, out _);
        if (held.Falling) return "the test cut felled the whole tower — that is not the case under test";
        if (held.Fracture is not { Version: > 0 })
            return "the test cut broke nothing — the tower is unmarked and nobody should let go";

        soldier.Update((float)Config.FixedDt, new Vector2(0f, 60f), 0f, city);
        if (soldier.Left.Anchored && ReferenceEquals(soldier.Left.Holding, held))
            return "the left cable is still holding a wall that has been shot out";
        if (soldier.Right.Anchored && ReferenceEquals(soldier.Right.Holding, held))
            return "the right cable is still holding a wall that has been shot out";

        // And they have to go and find another one, quickly. The cut tower is now the worse
        // of the two, so the honest answer is the other one.
        city.Standing = city.Far;
        int ticks = 0;
        for (; ticks < 60 * 2 && !soldier.AnyAnchored; ticks++)
            soldier.Update((float)Config.FixedDt, new Vector2(0f, 60f), 0f, city);

        if (!soldier.AnyAnchored)
            return "two seconds after losing their line they are still falling";
        if (ticks > 90)
            return $"it took them {ticks / 60f:0.0}s to find another wall — they are not re-anchoring, they are recovering";
        return null;
    }

    /// <summary>
    /// The rule that makes them a fight rather than a shooting gallery: they are never a
    /// stationary target. A pendulum that runs out of swing hangs, and a soldier hanging in
    /// the air taking pot-shots has thrown away every advantage the chassis has — so the
    /// rig forbids it outright and drops whatever line it is on rather than loiter.
    ///
    /// Measured as the worst case rather than the average, because an average hides exactly
    /// the thing that matters: one member parked in the air for four seconds is a free kill
    /// however busy the other three were.
    /// </summary>
    private static string? SoldiersNeverLoiter()
    {
        var world = SquadWorld();
        world.SpawnSoldierSquad();

        var still = new Dictionary<Entities.EnemySoldier, int>();
        int worst = 0;
        float fastest = 0f;
        double sum = 0;
        int samples = 0;

        for (int i = 0; i < 60 * 40 && world.Soldiers.Count > 0; i++)
        {
            StepWithoutInput(world);
            foreach (var s in world.Soldiers)
            {
                // Only while genuinely flying and genuinely able to fly. Walking is slow by
                // design, and a soldier who has just put themselves through a wall is
                // *supposed* to hang there with nothing to give — that window is the reward
                // for having flown them into it, not a failure of this rule.
                if (s.Height < 2f || s.Move == Entities.SoldierMove.Perched || s.Stagger > 0f)
                {
                    still[s] = 0;
                    continue;
                }

                sum += s.PlanarSpeed;
                samples++;
                fastest = MathF.Max(fastest, s.PlanarSpeed);

                // Measured on the whole velocity, not just the planar part: a soldier
                // dropping forty metres between anchors is doing something difficult to
                // shoot at, whatever their ground track says. What this is looking for is a
                // body that is simply *there*, in one place, for long enough to line up.
                int run = s.Velocity.Length() < 5f ? still.GetValueOrDefault(s) + 1 : 0;
                still[s] = run;
                worst = Math.Max(worst, run);
            }
        }

        if (samples == 0) return "the squad never got airborne, so there was nothing to measure";

        // The bar is derived rather than tuned until it went green, and it is worth stating
        // where it comes from, because it is not zero and cannot be. A swing that has
        // genuinely traded all its speed for height is slow at the top of the arc � that is
        // the physics working, and it is the same hang the player's own chassis gets � and
        // the guard's grace plus a whole apex � decelerating into the top of a climb and
        // accelerating back out of it under the eased hang pull � is a couple of seconds all
        // on its own, with nobody hovering anywhere. What this is policing is a soldier *parked*: hovering, holding
        // position, taking pot-shots off a rope. That has no floor and gets none.
        float parked = worst / 60f;
        if (parked > 3f)
            return $"one of them hung in the air going nowhere for {parked:0.0}s";

        float average = (float)(sum / samples);
        if (average < 9f)
            return $"they average {average:0.0} m/s in the air — that is drifting, not swinging";
        return null;
    }

    /// <summary>
    /// A city of exactly two towers, one near and one far, and a switch for which one it is
    /// willing to offer. Everything a flier can ask the world is one question, so a fake
    /// that answers that question is a whole world as far as the rig is concerned — which is
    /// the entire reason the query is an interface.
    /// </summary>
    private sealed class TwoTowers : Entities.IAnchorField
    {
        public readonly Structure Near = new(new Vector2(0f, 10f), 0f, StructureKind.Tower, 0, 1.5f);
        public readonly Structure Far = new(new Vector2(0f, 45f), 0f, StructureKind.Tower, 2, 1.5f);

        public Structure? Standing;

        public bool TryFindSwing(Vector3 from, Vector3 wish, float maxRange,
            out Vector3 point, out Structure? holding)
        {
            holding = Standing ??= Near;
            var at = new Vector2(holding.Position.X, holding.Position.Y);
            var delta = new Vector2(at.X - from.X, at.Y - from.Z);
            float d = delta.Length();
            if (d > maxRange || d < 1e-3f)
            {
                point = default;
                holding = null;
                return false;
            }

            // On the near face, well above them — the answer the real city gives.
            Vector2 surface = at - delta / d * 5f;
            point = new Vector3(surface.X, MathF.Max(from.Y + 8f, 28f), surface.Y);
            return true;
        }
    }

    /// <summary>A stage with nothing in it but the city and a squad — no hunters, no spawn
    /// director — so the checks above are measuring the squad and not a firefight.</summary>
    private static World.World SquadWorld()
    {
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    /// <summary>How far the nearest living soldier is from the player.</summary>
    private static float NearestSoldier(World.World world)
    {
        float best = float.MaxValue;
        foreach (var s in world.Soldiers)
            if (s.Alive) best = MathF.Min(best, Torus.Distance(s.Position, world.Player.Position));
        return best;
    }

    // --- The FISH ---------------------------------------------------------------

    /// <summary>
    /// The one fact about this chassis that has to be true before anything else can be
    /// tested: it is in the water. Every rule the class has — the lift, the beat, the
    /// bloom, the beaching — is a rule about a body that is off the ground, and a fish
    /// that opened lying on the grid would spend the player's first ten seconds in the one
    /// state the entire design is about escaping.
    /// </summary>
    private static string? FishStartsSwimming()
    {
        var world = FishWorld();
        var body = world.Player.Fish!;

        if (world.Player.Height < 10f)
            return $"a fish opened {world.Player.Height:0.0} off the grid";
        if (body.Beached) return "a fish opened beached";
        // And already moving: the class cannot hold altitude without speed, so opening
        // stationary would mean opening in a stall.
        if (body.PlanarSpeed < 1f) return "a fish opened dead in the water";

        // And the sink has to be gentle enough to answer. A player who takes their hands
        // off gets several seconds of drifting downward before anything bad happens — long
        // enough to read the depth ladder, understand what is going on and beat out of it.
        // Much less than this and the class would be a chore rather than a glide.
        for (int i = 0; i < 60 * 5; i++) StepWithoutInput(world);
        if (body.Beached) return "an idle fish hit the grid inside five seconds";
        return null;
    }

    /// <summary>
    /// The single decision the whole class rests on. A beat is an <em>impulse</em>: one
    /// press is one shove, mashing inside the refractory period buys nothing, and holding
    /// anything at all buys nothing either. If this ever degrades into a throttle the
    /// chassis becomes a slower aircraft and every other rule stops mattering.
    /// </summary>
    private static string? FishBeatIsAnImpulse()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        body.Velocity = Vector3.Zero;

        float before = body.Speed;
        if (!world.BeatFishForTest()) return "the first beat was refused";
        if (body.Speed < before + Entities.FishRig.BeatImpulse * 0.5f)
            return $"a beat only added {body.Speed - before:0.0} m/s";

        // Mashing: the next press inside the refractory period does nothing at all.
        if (world.BeatFishForTest()) return "a second beat landed inside the refractory period";

        // ...and one after it does. The gap is the rhythm the player is learning.
        for (int i = 0; i < (int)(Entities.FishRig.BeatInterval * 60f) + 2; i++)
            StepWithoutInput(world);
        if (!world.BeatFishForTest()) return "the tail never recovered between beats";

        // And with nothing pressed at all, the water takes it back. A body that coasted
        // forever would make the reserve decoration.
        float carried = body.Speed;
        for (int i = 0; i < 60 * 3; i++) StepWithoutInput(world);
        if (body.Speed >= carried)
            return "three seconds of coasting cost the body no speed at all";
        return null;
    }

    /// <summary>
    /// The economy. Beats cost breath, and breath comes back <em>only</em> while the tail
    /// is still — which is the rule that turns movement on this chassis into a rhythm
    /// rather than a key held down. Both halves have to hold: if beating were free the
    /// reserve would be decoration, and if the reserve refilled while beating there would
    /// be no reason ever to stop.
    /// </summary>
    private static string? FishBreathIsARhythm()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Hyper = p.MaxHyper;
        if (!world.BeatFishForTest()) return "the beat was refused";
        StepWithoutInput(world);

        float spent = p.Hyper;
        if (spent >= p.MaxHyper) return "a beat cost no breath";
        if (body.Recovering) return "breath started coming back the instant the tail moved";

        // Beating flat out for two seconds must genuinely run the reserve down rather
        // than being paid for out of the regen.
        for (int i = 0; i < 60 * 2; i++)
        {
            world.BeatFishForTest();
            StepWithoutInput(world);
        }
        if (p.Hyper >= spent)
            return "two seconds of continuous beating did not drain the reserve";

        // And coasting fills it. Nothing else does.
        float drained = p.Hyper;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (!body.Recovering) return "a coasting fish never starts recovering";
        if (p.Hyper <= drained) return "two seconds of coasting gave no breath back";
        return null;
    }

    /// <summary>
    /// The handling model. Momentum lags the crosshair, and how fast it catches up is set
    /// by how far the body is rolled — that gap, and the player's control over closing it,
    /// is the entire difference between this chassis and a strafing camera. A carve that
    /// turned no faster than a drift would make A and D pure decoration.
    /// </summary>
    private static string? FishCarveTurnsTighter()
    {
        float level = TurnedTowardLookIn(roll: 0f);
        float carved = TurnedTowardLookIn(roll: 1f);

        if (carved <= level + 0.05f)
            return $"a full carve converged to {carved:0.00} against a level drift's {level:0.00}";
        // And the level drift has to genuinely be a drift — a body that snapped onto the
        // crosshair without rolling would pass the comparison above and still be wrong.
        if (level > 0.98f)
            return "a level body turned onto the crosshair almost instantly — there is no drift";
        return null;
    }

    /// <summary>
    /// One second of turning ninety degrees onto the crosshair at a given roll, reported as
    /// how far the momentum got: 1 is fully converged, 0 is still travelling at a right
    /// angle to where the player is looking.
    /// </summary>
    private static float TurnedTowardLookIn(float roll)
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Travelling along +Z and looking along +X: a dead ninety degrees to turn through.
        p.Pitch = 0f;
        p.Heading = MathF.PI / 2f;
        body.Velocity = new Vector3(0f, 0f, 24f);
        body.MoveInput = new Vector2(roll, 0f);

        for (int i = 0; i < 60; i++) StepWithoutInput(world);

        return Vector3.Dot(Vector3.Normalize(body.Velocity), p.Forward3);
    }

    /// <summary>
    /// The altitude economy, and the class's answer to the soldier's height-for-speed
    /// trade: a body generates lift by moving, so speed is what keeps it up and the price
    /// of stalling is height. This is what makes running out of breath frightening rather
    /// than merely slow — the reserve going means the altitude goes with it.
    /// </summary>
    private static string? FishLiftHoldsAltitude()
    {
        float stalled = SankOverASecond(speed: 0f);
        float moving = SankOverASecond(speed: 34f);

        if (stalled <= 0.5f) return "a stalled fish did not sink at all";
        if (moving >= stalled * 0.6f)
            return $"a body at speed sank {moving:0.0}m against a stalled one's {stalled:0.0}m — lift is doing nothing";
        return null;
    }

    /// <summary>
    /// Metres lost in one second from a given planar speed, with nothing pressed. Measured
    /// down in clean water, well below the warned band — up in the thin stuff the lift is
    /// deliberately failing, which is the bloom's job and not this test's.
    /// </summary>
    private static float SankOverASecond(float speed)
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = Entities.FishRig.BloomWarnHeight - 9f;
        body.Velocity = new Vector3(0f, 0f, speed);

        float was = p.Height;
        for (int i = 0; i < 60; i++) StepWithoutInput(world);
        return was - p.Height;
    }

    /// <summary>
    /// The strike is a commitment, and the shape of that commitment is the whole balance of
    /// it: a gather where the body is nearly stopped and helpless, a lunge that is
    /// genuinely faster than anything swimming can reach, and a recovery that refuses to
    /// beat. Lose any of the three and it stops being a decision.
    /// </summary>
    private static string? FishStrikeCommits()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        body.Velocity = new Vector3(0f, 0f, 30f);

        if (!world.StrikeFishForTest()) return "the strike was refused on a full reserve";
        if (body.Strike != Entities.StrikeState.Coil) return "the strike did not begin by coiling";

        // The gather: momentum is scrubbed, and this is what the player pays before they
        // know whether the shot lands.
        for (int i = 0; i < (int)(Entities.FishRig.CoilTime * 60f) - 2; i++) StepWithoutInput(world);
        if (body.Speed > 12f) return $"the coil left {body.Speed:0.0} m/s on the clock — it costs nothing";

        for (int i = 0; i < 6 && body.Strike == Entities.StrikeState.Coil; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Lunge) return "the coil never loosed";
        if (body.Speed < Entities.FishRig.MaxSpeed)
            return $"the lunge travels at {body.Speed:0.0}, no faster than swimming does";

        // And the bill: spent, and unable to beat its way out of the recovery.
        for (int i = 0; i < (int)(Entities.FishRig.LungeTime * 60f) + 4; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Recover) return "the lunge never ended";
        if (world.BeatFishForTest()) return "a spent fish could beat straight out of its recovery";

        for (int i = 0; i < (int)(Entities.FishRig.RecoverTime * 60f) + 4; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Ready) return "the strike never came back";
        return null;
    }

    /// <summary>
    /// A strike spears one thing. It is not a plough that clears a street — if a single
    /// lunge could rake a whole group the correct play would be to fly through crowds, and
    /// the attack would stop being about picking a target.
    /// </summary>
    private static string? FishStrikeSpearsOnce()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Down at hunter height, looking along +Z, with two of them in a line dead ahead.
        p.Pitch = 0f;
        p.Heading = 0f;
        p.Height = 1.5f;
        p.Position = Vector2.Zero;
        body.Velocity = Vector3.Zero;

        var near = new Entities.EnemyTank(new Vector2(0f, 9f), elite: false);
        var far = new Entities.EnemyTank(new Vector2(0f, 17f), elite: false);
        world.Enemies.Add(near);
        world.Enemies.Add(far);

        if (!world.StrikeFishForTest()) return "the strike was refused";

        // Long enough for the whole lunge to run through both of them.
        int steps = (int)((Entities.FishRig.CoilTime + Entities.FishRig.LungeTime) * 60f) + 6;
        for (int i = 0; i < steps; i++) StepWithoutInput(world);

        if (near.Alive) return "the strike passed through a hunter without hurting it";
        if (!far.Alive) return "one strike killed two hunters — the lunge is a plough";
        return null;
    }

    /// <summary>
    /// The floor. Meeting the grid is not a landing on this chassis, it is a fish out of
    /// water — and the important half of the rule is that it is <em>recoverable</em>: a
    /// beached player must be able to beat their way back up, or the state is a death
    /// sentence rather than a punishment.
    /// </summary>
    private static string? FishBeachesAndRecovers()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = 3f;
        body.Velocity = new Vector3(0f, -6f, 0f);

        for (int i = 0; i < 60 * 2 && !body.Beached; i++) StepWithoutInput(world);
        if (!body.Beached) return "a fish driven into the grid never beached";
        if (p.Height > 0.01f) return "a beached fish is not actually on the grid";

        // On the deck it can barely move. That is the whole punishment, and a beached body
        // that went on skating at speed would read as landing badly rather than as being
        // out of its element. Measured over half a second, which is the window in which a
        // player decides whether they are in trouble.
        body.Velocity = new Vector3(20f, 0f, 0f);
        for (int i = 0; i < 30; i++) StepWithoutInput(world);
        if (body.PlanarSpeed > 1f)
            return $"a beached fish still carries {body.PlanarSpeed:0.0} m/s after half a second";

        // Now beat out of it. Nose up, wait out any stagger, and take a couple of flops.
        p.Pitch = 0.5f;
        for (int i = 0; i < 60 * 4; i++)
        {
            world.BeatFishForTest();
            StepWithoutInput(world);
            if (!body.Beached) break;
        }
        if (body.Beached) return "a beached fish could not beat its way back into the water";
        return null;
    }

    /// <summary>
    /// The ceiling, and the half of it that matters most: the player is told, for free, a
    /// long way before anything costs them. Ten metres of warned, thinning water is enough
    /// to turn round in at any speed the class can reach — and if that band ever started
    /// billing, the warning would just be a cheaper way of being hurt.
    /// </summary>
    private static string? FishBloomWarnsFirst()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Parked in the middle of the warned band, held there against the sink.
        p.Pitch = 0f;
        float shield = p.Shield;

        for (int i = 0; i < 60 * 3; i++)
        {
            p.Height = (Entities.FishRig.BloomWarnHeight + Entities.FishRig.BloomHeight) * 0.5f;
            StepWithoutInput(world);
        }

        if (body.BloomNotice <= 0f) return "the warned band never raised a notice";
        if (body.InBloom) return "the middle of the warned band already counts as the bloom";
        if (body.Toxicity > 0f) return "the warned band reports toxicity";
        if (p.Shield < shield) return "three seconds in the warned band cost shield";

        // And nothing at all below it, so ordinary play at skyline height is never nagged.
        p.Height = Entities.FishRig.BloomWarnHeight - 6f;
        StepWithoutInput(world);
        if (body.BloomNotice > 0f) return "the notice fires below the warned band";
        return null;
    }

    /// <summary>
    /// And the other half: past the warning it genuinely bites, and the water up there is
    /// genuinely thinner. The thinning is the more important of the two — it is a fence the
    /// player feels through the controls, which means a fish that stops fighting sinks out
    /// of the bloom on its own rather than needing to be told to.
    /// </summary>
    private static string? FishBloomHurts()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = Entities.FishRig.BloomHeight + 8f;
        body.Velocity = new Vector3(0f, 0f, 30f);

        float shield = p.Shield;
        float was = p.Height;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);

        if (!body.InBloom && p.Height > Entities.FishRig.BloomHeight)
            return "the body is above the bloom's floor but not in it";
        if (p.Shield >= shield) return "two seconds inside the bloom cost no shield";

        // Thin water: a body moving this fast holds its altitude easily down in the clean
        // water, and must not up here.
        if (p.Height >= was)
            return "the body held its altitude inside the bloom — the water is not thinning";
        return null;
    }

    /// <summary>
    /// The same property the soldier's rifle has to have, for the same reason: this is a
    /// mouse-aimed weapon, so the aim, the eye, the muzzle offset and the round's own climb
    /// are four separate pieces of arithmetic, and any one of them being off is invisible
    /// standing still and infuriating mid-carve. Checked at steep pitches especially, since
    /// the failure it guards against — treating a look slope as a look angle — is nearly
    /// exact at level and badly wrong the moment the player looks up.
    /// </summary>
    private static string? FishSpitFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = FishWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // High enough that a steeply downward shot has some flight to measure before
            // it correctly buries itself in the grid, low enough to stay in clean water.
            p.Height = Entities.FishRig.BloomWarnHeight - 9f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireFishSpitForTest();
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the water at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the spit flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>A stage with a fish in it and nothing else moving: no spawn director, no
    /// hunters, so the checks above are measuring the body and not a firefight.</summary>
    private static World.World FishWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Fish };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    // --- The VIRUS: the mote and the bodies it wears -----------------------------

    /// <summary>
    /// The class's opening state and its one unique freedom. A virus starts exposed — the
    /// naked mote, no host — and the mote flies where the crosshair points, which no other
    /// chassis's W can claim: nose up and drive, and it gains genuine altitude with no
    /// impulse, no arc and no cable involved.
    /// </summary>
    private static string? VirusMoteFliesWhereItLooks()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        if (!v.Exposed) return "a virus did not open exposed";
        if (v.Hosted) return "a virus opened already wearing a body";

        // Nose up the look and hold thrust: the mote climbs and travels along the bearing.
        p.Pitch = 0.6f;
        p.Heading = 0f;
        v.MoveInput = new Vector2(0f, 1f);
        Vector2 start = p.Position;

        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);

        if (p.Height < 4f)
            return $"two seconds of flying up the look only gained {p.Height:0.0}m";
        Vector2 gone = Torus.Delta(start, p.Position);
        if (gone.Y < 4f)
            return $"the mote only covered {gone.Y:0.0}m along the bearing it was flown on";

        // And letting go coasts it down, not on forever: drag is what keeps the mote a
        // creature rather than a projectile.
        v.MoveInput = Vector2.Zero;
        float carried = v.Speed;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (v.Speed >= carried * 0.6f)
            return "two seconds of coasting cost the mote almost none of its speed";
        return null;
    }

    /// <summary>
    /// The infection. Contact is the whole mechanic — flying the mote into a hunter wears
    /// it, with no button to press — and the body is <em>consumed</em>, not killed: it
    /// leaves the roster without a death, and the player is standing where it stood with a
    /// full decay meter on the clock.
    /// </summary>
    private static string? VirusInfectsOnContact()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        var prey = new Entities.EnemyTank(p.Position + new Vector2(0f, 2f), elite: false);
        Vector2 stood = prey.Position;
        world.Enemies.Add(prey);
        StepWithoutInput(world);

        if (!v.Hosted) return "contact with a hunter did not possess it";
        if (world.Enemies.Count != 0) return "the worn hunter is still on the roster";
        if (v.Decay < 0.99f) return $"a fresh host opened at {v.Decay:0.00} of its meter";
        if (Torus.Distance(p.Position, stood) > 0.1f)
            return "the player did not climb into where the body stood";
        return null;
    }

    /// <summary>
    /// The clock. A worn host rots out on time alone and ejects the mote — the rule that
    /// makes a body a countdown rather than a home — and rotting out costs the player's own
    /// shield nothing at all: the meter is the only thing the clock spends.
    /// </summary>
    private static string? VirusHostRots()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        PossessFreshHost(world);
        if (!v.Hosted) return "the mote never took its host";
        float shield = p.Shield;

        int i = 0;
        for (; i < 60 * 70 && v.Hosted; i++) StepWithoutInput(world);

        if (v.Hosted) return "the host never rotted out";
        // The advertised lifetime is most of a minute — a body that gave out in a few
        // seconds untouched would make hosting pointless, and the clock is meant to be
        // the rare way a host ends, not the usual one.
        if (i < 60 * 30) return $"a clean host lasted only {i / 60f:0.0}s";
        if (p.Shield < shield) return "rotting out cost the player shield";
        if (!v.Exposed) return "the ejected mote is not exposed";
        return null;
    }

    /// <summary>
    /// The armour. While a host is worn, damage drains the host's meter and the player's
    /// shield never sees it — proved by racing two identical hosted worlds, one under fire:
    /// after the same time the shot-at host is visibly more rotten and both shields are
    /// untouched. Plus the rig-level contract for a blow bigger than the whole body: the
    /// husk breaks and only the overflow comes through.
    /// </summary>
    private static string? VirusHostSoaksDamage()
    {
        var under = VirusWorld();
        var calm = VirusWorld();
        PossessFreshHost(under);
        PossessFreshHost(calm);
        if (under.Player.Virus is not { Hosted: true } shot) return "the mote never took its host";
        if (calm.Player.Virus is not { Hosted: true } idle) return "the control mote never took its host";

        // The shooter, held at a stand-off range where its own AI keeps it — far outside
        // the infect reach, close enough to land several hits over the window.
        under.Enemies.Add(new Entities.EnemyTank(new Vector2(0f, 30f), elite: false));

        float max = under.Player.MaxShield;
        for (int i = 0; i < 60 * 8; i++)
        {
            StepWithoutInput(under);
            StepWithoutInput(calm);
        }

        if (!shot.Hosted) return "eight seconds of hunter fire broke a whole host";
        if (under.Player.Shield < max) return "a hosted player's shield was touched under fire";
        if (shot.Decay >= idle.Decay - 0.02f)
            return $"fire left the host at {shot.Decay:0.00} against the clock's own {idle.Decay:0.00} — nothing was soaked";

        // And the overflow contract, at the rig: a blow past the body's whole capacity
        // breaks it and passes only the remainder through.
        var rig = new Entities.VirusRig();
        var dummy = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Virus });
        rig.Possess(dummy, Entities.VirusHost.Hunter);
        float over = rig.AbsorbDamage(10_000f);
        if (rig.Hosted) return "a blow bigger than the whole body left the host standing";
        if (over <= 0f || over >= 10_000f)
            return $"a broken husk passed {over:0} of a 10000 blow through";
        return null;
    }

    /// <summary>
    /// The price. A naked mote takes hits amplified — the same hunter's round costs it well
    /// over what a standard craft pays, which is the number the whole hop-or-die tension
    /// hangs on. Measured against a tank control under identical fire rather than against a
    /// copied constant, so retuning either side moves this check with it.
    /// </summary>
    private static string? VirusMoteIsFragile()
    {
        float moteLoss = FirstHitCost(new Loadout { Class = PlayerClass.Virus });
        float tankLoss = FirstHitCost(new Loadout { Class = PlayerClass.Tank });

        if (tankLoss <= 0f) return "no shot ever landed on the control tank";
        if (moteLoss <= 0f) return "no shot ever landed on the exposed mote";
        if (moteLoss < tankLoss * 1.5f)
            return $"an exposed hit cost {moteLoss:0.0} against a tank's {tankLoss:0.0} — the mote is not fragile";
        return null;
    }

    /// <summary>Shield lost to the first landed hit, for a given build standing still under
    /// one hunter's fire at stand-off range.</summary>
    private static float FirstHitCost(Loadout loadout)
    {
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Enemies.Add(new Entities.EnemyTank(new Vector2(0f, 30f), elite: false));

        float max = world.Player.MaxShield;
        for (int i = 0; i < 60 * 15 && world.Player.Shield >= max; i++)
            StepWithoutInput(world);
        return max - world.Player.Shield;
    }

    /// <summary>
    /// The heavy. An overload spends the worn host outright: the player is ejected on the
    /// spot and the body goes off as the radial blast, which kills what is standing near
    /// it. And the other half of the guard — with no host there is nothing to spend, so the
    /// trigger stages nothing at all.
    /// </summary>
    private static string? VirusOverloadSpendsTheHost()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        // Exposed, the overload must refuse: no host, no bomb.
        world.OverloadVirusForTest();
        if (world.Blasts.Count != 0) return "an overload with no host staged a blast";

        PossessFreshHost(world);
        if (!v.Hosted) return "the mote never took its host";

        var victim = new Entities.EnemyTank(p.Position + new Vector2(0f, 8f), elite: false);
        world.Enemies.Add(victim);

        world.OverloadVirusForTest();
        if (!v.Exposed) return "the overload did not eject the player";
        if (world.Blasts.Count != 1) return $"the overload staged {world.Blasts.Count} blasts";

        for (int i = 0; i < 240 && victim.Alive; i++) StepWithoutInput(world);
        if (victim.Alive) return "the spent host's blast left a point-blank hunter standing";
        return null;
    }

    /// <summary>
    /// The same property the rifle and the spit have to have, for the same reason: a
    /// mouse-aimed weapon whose aim, eye, muzzle offset and flight are four separate pieces
    /// of arithmetic. Checked at steep pitches especially — the mote fires down its whole
    /// flight line, so "up" is not a rare case on this chassis, it is the ordinary one.
    /// </summary>
    private static string? VirusRoundFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = VirusWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // Up in the mote's own air, so a steeply downward round has flight to measure
            // before it correctly buries itself in the grid.
            p.Height = 20f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireVirusRoundForTest();
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the air at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the round flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>
    /// The mote's own clock. The grace is genuinely free — a mote can sit exposed for
    /// nearly all of it and never be billed — and past it the withering bites until a body
    /// is reached, at which point the clock is handed back whole. Lose either half and the
    /// state stops meaning what it says: a grace that bills is just damage with a delay,
    /// and a withering that possession doesn't cure is a death sentence with extra steps.
    /// </summary>
    private static string? VirusWithersAfterGrace()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        // Most of the grace, sitting still in the open: free.
        float max = p.MaxShield;
        int safe = (int)((Entities.VirusRig.ExposureGrace - 1f) * 60f);
        for (int i = 0; i < safe; i++) StepWithoutInput(world);
        if (v.Withering) return "the withering started inside the grace";
        if (p.Shield < max) return "the mote was billed inside its grace";

        // Past it: the bites start and keep coming.
        for (int i = 0; i < 60 * 4; i++) StepWithoutInput(world);
        if (!v.Withering) return "the grace ran out and nothing started";
        if (p.Shield >= max) return "four seconds of withering cost no shield";

        // And a body ends it on the spot.
        PossessFreshHost(world);
        if (!v.Hosted) return "a withering mote could not take a host";
        if (v.Withering) return "possession did not stop the withering";
        float shield = p.Shield;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (p.Shield < shield) return "a hosted mote went on withering";
        return null;
    }

    /// <summary>
    /// The big prize. The Crab-Core is entered through its gem — the same weak point a
    /// bullet needs, used as a door — it leaves the field worn rather than wrecked, and
    /// the weapon that comes with it is its own lance gone wrong: one pull costs a slice
    /// of the meter and leaves several shafts burning, and the aimed one stays honest
    /// enough to kill what the crosshair was actually on.
    /// </summary>
    private static string? VirusWearsTheCrab()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        world.SpawnCrabAhead();
        if (world.Boss is not { } crab) return "no crab was raised to wear";

        // Fly the mote into the gem.
        p.Position = crab.Position;
        p.Height = Entities.CrabCore.CoreHitHeight;
        StepWithoutInput(world);

        if (v.HostKind != Entities.VirusHost.Crab)
            return "standing in the core did not possess the crab";
        if (world.Boss != null) return "the worn crab is still on the field";

        // Let the body settle onto the grid, then stand a hunter out in front and fire
        // the lance down at it from the crab's high eye.
        for (int i = 0; i < 90; i++) StepWithoutInput(world);

        var victim = new Entities.EnemyTank(
            Torus.Wrap(p.Position + new Vector2(0f, 18f)), elite: false);
        world.Enemies.Add(victim);
        p.Heading = 0f;
        p.Pitch = MathF.Atan2(Entities.EnemyTank.AimHeight - p.EyeHeight, 18f);

        float decay = v.Decay;
        world.FireVirusLanceForTest();

        if (v.Decay >= decay) return "a lance discharge cost the host nothing";
        int shafts = 0;
        foreach (var s in v.Shafts) if (s.Life > 0f) shafts++;
        if (shafts < 3) return $"the broken lance left only {shafts} shafts burning";
        if (victim.Alive) return "the aimed shaft missed the hunter under the crosshair";
        return null;
    }

    /// <summary>
    /// The other one. The Maw-Core is entered through its crystal, and what you get is the
    /// monster's own life: a body that hovers — two seconds untouched and it has not sunk
    /// to the grid — and a spit that leaves as the mouth's acid bolt rather than a
    /// re-tinted rifle round.
    /// </summary>
    private static string? VirusWearsTheMaw()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        world.SpawnMawAhead();
        if (world.Maw is not { } mouth) return "no maw was raised to wear";

        p.Position = mouth.Position;
        p.Height = Entities.MawRig.CrystalWorldY;
        StepWithoutInput(world);

        if (v.HostKind != Entities.VirusHost.Maw)
            return "standing in the crystal did not possess the maw";
        if (world.Maw != null) return "the worn maw is still on the field";

        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (p.Height < 1.5f) return $"a worn maw sank to {p.Height:0.0} — it is not hovering";

        world.FireVirusRoundForTest();
        StepWithoutInput(world);
        foreach (var q in world.Projectiles)
            if (q.Active && q.IsAcid) return null;
        return "the worn maw's spit is not an acid bolt";
    }

    // --- The plague: what a VIRUS does to people -------------------------------------

    /// <summary>
    /// The round that changed. Against a machine the mote's bolt is a bolt; against a person
    /// it is an infection, and the seed has to land — otherwise every rule below it is
    /// unreachable. Checked through the world's own fire path against a live soldier, so
    /// what is measured is the thing the player actually does.
    /// </summary>
    private static string? VirusSeedsASoldier()
    {
        var world = VirusWorld();
        var p = world.Player;

        var mark = PlantSoldier(world, ahead: 14f);
        p.Pitch = MathF.Atan2(mark.Height + Entities.EnemySoldier.AimHeight - p.Eye.Y, 14f);

        for (int i = 0; i < 60 * 2 && mark.Tagged <= 0f && mark.Alive; i++)
        {
            world.FireVirusRoundForTest();
            StepWithoutInput(world);
        }

        if (!mark.Alive) return "the round killed them outright — nothing was seeded";
        if (mark.Tagged <= 0f) return "two seconds of fire and the corruption never took";
        if (mark.Carrier) return "the seed rooted on the tick it landed — there is no window";
        return null;
    }

    /// <summary>
    /// And what happens when the mote does not come to collect. The seed roots on its own,
    /// and the body it roots in stops being the squad's — which is the whole twist: a shot
    /// you fire and then ignore is not a wasted shot, it is an ally with a short life.
    /// </summary>
    private static string? SeedRootsIntoACarrier()
    {
        var world = SquadWorld();
        world.SpawnSoldierSquad();
        var squad = world.Squads[0];
        var mark = world.Soldiers[0];

        mark.Tag();
        int had = squad.Members.Count;

        for (int i = 0; i < 60 * 5 && !mark.Carrier; i++) StepWithoutInput(world);
        // One more, because the squads think at the top of a step and the soldiers turn
        // inside it: the tick a seed roots is a tick its old squad has already been through.
        StepWithoutInput(world);

        if (!mark.Carrier) return "five seconds on, the seed still had not rooted";
        if (squad.Members.Contains(mark)) return "the squad is still giving orders to a carrier";
        if (squad.Members.Count != had - 1)
            return $"the squad went from {had} to {squad.Members.Count} — it lost the wrong number";
        if (!world.Soldiers.Contains(mark)) return "the carrier fell off the field entirely";
        return null;
    }

    /// <summary>
    /// Wearing a person. Two things have to be true and the second is the interesting one:
    /// the mote takes them on contact — no ceremony, no seed required, the same rule a
    /// hunter has always been taken by — and what it gets is their <em>kit</em>. A worn
    /// soldier with no cables would be a slow hunter with a worse meter.
    /// </summary>
    private static string? VirusWearsASoldier()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        var mark = PlantSoldier(world, ahead: 3f);

        for (int i = 0; i < 30 && !v.Hosted; i++) StepWithoutInput(world);

        if (!v.Hosted) return "a soldier at arm's length was never taken";
        if (v.HostKind != Entities.VirusHost.Soldier)
            return $"taking a soldier produced a {v.HostKind} host";
        if (v.WornRig is null) return "the body was worn but its launchers were not";
        if (p.Rig is null) return "nothing downstream can see the worn rig";
        if (world.Soldiers.Contains(mark)) return "the worn body is still on the roster";

        // And the kit has to work: a hook thrown from a stolen launcher has to bite the city
        // exactly as the chassis that owns one does.
        var rig = v.WornRig!;
        p.Pitch = 0.2f;
        Vector3 from = world.SoldierMuzzle(right: true);
        if (world.TryFindSwing(from, p.Forward3, Entities.SoldierRig.MaxRange,
                out Vector3 at, out Structure? holding))
        {
            rig.FireHook(true, new Vector2(from.X, from.Z), from.Y,
                Vector3.Normalize(at - from), at, holding);
            for (int i = 0; i < 60 * 3 && !rig.Right.Anchored && v.Hosted; i++)
                StepWithoutInput(world);
            if (v.Hosted && !rig.Right.Anchored)
                return "a hook thrown from the stolen kit never bit anything";
        }
        return null;
    }

    /// <summary>
    /// The heavy, reread. An overload used to be a bomb; against people it is an outbreak —
    /// the body you were going to lose anyway, spent to turn everyone standing near it. This
    /// is the mechanic's best moment and it should be worth the host every time.
    /// </summary>
    private static string? OverloadSpreadsThePlague()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        // A hunter to wear, and three soldiers standing in the blast.
        world.Enemies.Add(new Entities.EnemyTank(p.Position, elite: false));
        StepWithoutInput(world);
        if (!v.Hosted) return "the setup never got a host to spend";

        var near = new List<Entities.EnemySoldier>();
        for (int i = 0; i < 3; i++)
        {
            var s = new Entities.EnemySoldier(
                Torus.Wrap(p.Position + new Vector2(3f + i * 2f, 0f)), 2f, leader: i == 0, slot: i);
            world.Soldiers.Add(s);
            near.Add(s);
        }

        world.OverloadVirusForTest();

        foreach (var s in near)
            if (!s.Carrier)
                return "a soldier standing in an overload was not turned by it";
        // The hunter is gone whatever happens next. What happens next is often that the mote
        // lands in one of the bodies it has just turned, which is not a failure — it is the
        // combination the mechanic was built to allow — so the check is that the *hunter*
        // was spent rather than that the player ended up with nothing.
        if (v.HostKind == Entities.VirusHost.Hunter)
            return "the overload did not spend the host";
        return null;
    }

    /// <summary>
    /// The end of one. A carrier is a body running on stolen code with nothing maintaining
    /// it; with nobody left of its own side to spend that on, it has to come apart rather
    /// than circle a player it has no quarrel with. Guards against the plague quietly
    /// becoming a permanent escort.
    /// </summary>
    private static string? CarrierStarvesOut()
    {
        var world = SquadWorld();
        var lone = PlantSoldier(world, ahead: 20f);
        lone.Turn();

        for (int i = 0; i < 60 * 8 && lone.Alive; i++) StepWithoutInput(world);

        return lone.Alive
            ? $"a carrier with no side left to fight was still going after eight seconds ({lone.Shield:0.0} shield)"
            : null;
    }

    /// <summary>
    /// The grab. Contact alone against something crossing the sky at thirty-four metres a
    /// second is a coin toss dressed up as a skill, so the player gets to <em>ask</em>: press
    /// the key anywhere near a body and the mote throws itself at it. What is checked here is
    /// the gap between the two reaches — a soldier well past passive touch, close enough that
    /// a player would say "that one", and the grab has to answer.
    /// </summary>
    private static string? VirusLungeCatchesASoldier()
    {
        var world = VirusWorld();
        var v = world.Player.Virus!;

        // Comfortably past anything contact would ever pick up, comfortably inside a lunge.
        var mark = PlantSoldier(world, ahead: 13f);

        StepWithoutInput(world);
        if (v.Hosted) return "a body thirteen metres off was taken by passive contact alone";

        world.LungeAtSoldierForTest();

        if (!v.Hosted) return "the grab did not reach a body thirteen metres away";
        if (v.HostKind != Entities.VirusHost.Soldier)
            return $"the grab produced a {v.HostKind} host";
        if (world.Soldiers.Contains(mark)) return "the grabbed body is still on the roster";
        return null;
    }

    /// <summary>
    /// The other half of being blind. A payload with no body cannot see matter because it
    /// does not touch matter — so an exposed mote flies straight through the city, and the
    /// instant it takes a body the world becomes solid again. Both directions are checked
    /// against the same wall, because a rule that only worked one way would be the cruellest
    /// possible version of this: unable to see the buildings and still stopped by them.
    /// </summary>
    private static string? ExposedMoteIsIncorporeal()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        Structure? tower = null;
        foreach (var s in world.Structures)
            if (s.Kind == StructureKind.Tower && !s.Falling) { tower = s; break; }
        if (tower is null) return "a razed city — nothing to pass through";

        // Stand the mote in the middle of the tower's footprint, low enough to be inside it.
        p.Position = tower.Position;
        p.Height = 4f;
        StepWithoutInput(world);

        float shoved = Torus.Distance(p.Position, tower.Position);
        if (shoved > 0.5f)
            return $"an exposed mote was pushed {shoved:0.0} out of a wall it cannot even see";

        // Now with a body on. The same wall has to shove exactly as it always did.
        world.Enemies.Add(new Entities.EnemyTank(p.Position, elite: false));
        StepWithoutInput(world);
        if (!v.Hosted) return "the setup failed to take a host";

        p.Position = tower.Position;
        StepWithoutInput(world);

        if (Torus.Distance(p.Position, tower.Position) < 0.5f)
            return "a worn body walked through a tower — the world is solid again or nothing is";
        return null;
    }

    /// <summary>
    /// The disguise. Wearing one of a squad's own bodies, there is nothing for the other
    /// three to see — same kit, same silhouette, same sky — so they go back to their walls
    /// and the player swings through the middle of them. And the moment the corruption
    /// leaves your hands, they know.
    ///
    /// Both halves matter. A disguise that never broke would be an off switch for the enemy.
    /// </summary>
    private static string? WornSoldierPassesForOneOfThem()
    {
        var world = VirusWorld();
        var v = world.Player.Virus!;

        world.SpawnSoldierSquad();
        var squad = world.Squads[0];

        // A body to wear, taken by contact the way any of them are.
        PlantSoldier(world, ahead: 2f);
        for (int i = 0; i < 60 && !v.Hosted; i++) StepWithoutInput(world);
        if (v.HostKind != Entities.VirusHost.Soldier) return "the setup never got a body on";

        if (!world.PlayerPassesForOneOfThem) return "wearing one of them is not a disguise at all";

        // Five seconds of standing in plain sight. They should never once call.
        for (int i = 0; i < 60 * 5; i++)
        {
            StepWithoutInput(world);
            if (!v.Hosted) return "the body rotted out before the check could finish";
            if (squad.Alerted) return "the squad called on a player wearing one of their own";
        }

        // And now show them what is driving it.
        world.FireVirusRoundForTest();
        if (world.PlayerPassesForOneOfThem)
            return "firing the corruption out of a stolen body did not give it away";

        for (int i = 0; i < 60 * 3 && !squad.Alerted; i++) StepWithoutInput(world);
        if (!squad.Alerted) return "cover was blown and the squad still never noticed";
        return null;
    }

    /// <summary>
    /// The escort. Take a body out of the middle of a squad and the survivors inherit you:
    /// from their side one of the four is still right there in the same kit. They fly cover,
    /// they keep formation, and they stop being three quarters of the thing that was killing
    /// you. And it lasts exactly as long as the body does — take the host off and the people
    /// beside you have just watched a mote climb out of their friend.
    /// </summary>
    private static string? SquadEscortsTheWornBody()
    {
        var world = EscortedWorld(out var squad, out _);
        if (world is null) return "the setup never got a body out of a squad";

        if (world.Escort != squad) return "taking one of a squad's own did not buy their cover";
        if (!squad.Escorting) return "the squad does not know it is escorting anybody";
        foreach (var m in squad.Members)
            if (!m.Allied) return "a member of an escorting squad is not on the player's side";

        // They have to stay with you rather than wander off. Measured over a few seconds of
        // the player standing still: the ring should keep them close.
        var p = world.Player;
        for (int i = 0; i < 60 * 4; i++) StepWithoutInput(world);
        if (p.Virus!.HostKind != Entities.VirusHost.Soldier)
            return "the worn body rotted out before the check finished";

        float nearest = float.MaxValue;
        foreach (var m in squad.Members)
            nearest = MathF.Min(nearest, Torus.Distance(m.Position, p.Position));
        if (nearest > Entities.EnemySoldier.EngageRange * 2.5f)
            return $"four seconds on, the nearest escort is {nearest:0} out — they are not following";

        // And it dies with the body. Spend the host and they are strangers again.
        world.OverloadVirusForTest();
        StepWithoutInput(world);
        if (world.Escort != null) return "the escort outlived the body that bought it";
        return null;
    }

    /// <summary>
    /// What an escort does with itself. Given an enemy they shoot it — with rounds that
    /// genuinely land on the other side, which is the entire point of turning them — and
    /// given nobody at all they hold formation and hold their fire, rather than pouring
    /// rifle rounds into the comrade they are supposed to be covering.
    /// </summary>
    private static string? EscortPicksItsFights()
    {
        var world = EscortedWorld(out var squad, out _);
        if (world is null) return "the setup never got a body out of a squad";
        var p = world.Player;

        // Nothing to fight. Four seconds of formation, and not one trigger pulled: with the
        // player as the only thing in the sky, any round at all is a round at them.
        //
        // Counted as shots fired rather than as shield lost, deliberately. An escort's round
        // travels as the player's and therefore *cannot* hit them — that falls out of the
        // routing — so measuring the player's shield would be measuring nothing, and would
        // quietly pass even if all three opened up.
        int shotsWithNoEnemy = 0;
        for (int i = 0; i < 60 * 4 && p.Virus!.Hosted; i++)
        {
            StepWithoutInput(world);
            foreach (var m in squad.Members) if (m.JustFired) shotsWithNoEnemy++;
        }
        if (shotsWithNoEnemy > 0)
            return $"an escort with nothing to shoot at fired {shotsWithNoEnemy} rounds anyway";

        // Now give them something, parked where the ring already sweeps.
        var prey = new Entities.EnemyTank(Torus.Wrap(p.Position + new Vector2(0f, 26f)),
            elite: false, shieldBonus: 40);
        world.Enemies.Add(prey);

        float had = prey.Shield;
        int shotsAtEnemy = 0;
        for (int i = 0; i < 60 * 14 && prey.Alive && p.Virus!.Hosted; i++)
        {
            StepWithoutInput(world);
            foreach (var m in squad.Members) if (m.JustFired) shotsAtEnemy++;
        }

        if (shotsAtEnemy == 0) return "an escort with a hunter in front of it never fired";
        if (prey.Alive && prey.Shield >= had)
            return "the escort fired at a hunter for fourteen seconds and never hit it";
        return null;
    }

    /// <summary>
    /// The one thing that ends it early. An escort is the only asset in this game the player
    /// can lose through carelessness rather than through being beaten, and putting a round
    /// into one of them has to be exactly that expensive — otherwise the whole arrangement is
    /// a free three-man gun crew with no way to squander it.
    /// </summary>
    private static string? BetrayedEscortTurns()
    {
        var world = EscortedWorld(out var squad, out _);
        if (world is null) return "the setup never got a body out of a squad";
        if (squad.Members.Count == 0) return "an escort of nobody";

        var victim = squad.Members[0];
        world.HurtSoldierForTest(victim, 0.5f);

        if (world.Escort != null) return "shooting one of the escort cost nothing";
        if (world.PlayerPassesForOneOfThem) return "the disguise survived shooting one of them";

        StepWithoutInput(world);
        foreach (var m in squad.Members)
            if (m.Allied) return "a betrayed squad is still flying cover";
        return null;
    }

    /// <summary>
    /// A virus, a squad, and one of that squad's own bodies already on. Returns null if the
    /// possession never happened, so each caller reports that rather than dereferencing its
    /// way into a confusing failure.
    /// </summary>
    private static World.World? EscortedWorld(out Entities.SoldierSquad squad,
        out Entities.EnemySoldier taken)
    {
        var world = VirusWorld();
        world.SpawnSoldierSquad();
        squad = world.Squads[0];

        // Stand the whole squad on the player so the nearest of them is taken by contact —
        // it has to be one of *theirs* for the escort to be inherited from anybody.
        taken = squad.Members[0];
        for (int i = 0; i < squad.Members.Count; i++)
            squad.Members[i].Position = Torus.Wrap(world.Player.Position
                + new Vector2(2f + i * 4f, 0f));
        foreach (var m in squad.Members) m.Height = world.Player.Height;

        for (int i = 0; i < 60 && !world.Player.Virus!.Hosted; i++) StepWithoutInput(world);
        return world.Player.Virus!.HostKind == Entities.VirusHost.Soldier ? world : null;
    }

    /// <summary>
    /// A regression, and a pointed one. The hull's view shake used to ring down only inside
    /// the tank's own step — which was fine for exactly as long as nothing else raised it.
    /// The moment something did (a soldier's blade landing on a player who was not in a
    /// tank), it pinned at whatever it was set to and shook the camera for the rest of the
    /// run, because no path that chassis ever took came back to clear it.
    ///
    /// Screen feedback has to settle for whoever is driving. Checked on the chassis that
    /// cannot reach the tank's integrator at all.
    /// </summary>
    private static string? ShakeAlwaysSettles()
    {
        var world = VirusWorld();
        var p = world.Player;

        p.Jolt(1f);
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);

        if (p.Shake > 0f)
            return $"two seconds on, a virus is still being shaken at {p.Shake:0.00}";

        // And through a set piece as well: a hold that freezes the ring-down hands the
        // player back a camera that never stops.
        p.Jolt(1f);
        p.Captured = true;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        p.Captured = false;

        return p.Shake > 0f
            ? $"a shake started before a cinematic never settled ({p.Shake:0.00})"
            : null;
    }

    /// <summary>Drops one soldier a fixed distance ahead of the player, at head height and
    /// with nothing else on the field, so the checks above are measuring one body.</summary>
    private static Entities.EnemySoldier PlantSoldier(World.World world, float ahead)
    {
        var p = world.Player;
        var at = Torus.Wrap(p.Position + p.Forward * ahead);
        var s = new Entities.EnemySoldier(at, MathF.Max(0f, p.Height), leader: false, slot: 0);
        world.Soldiers.Add(s);
        return s;
    }

    /// <summary>A stage with a virus in it and nothing else moving, so the checks above are
    /// measuring the mote and not a firefight.</summary>
    private static World.World VirusWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Virus };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    /// <summary>Parks a hunter on the player and steps once, so the mote takes it — the
    /// standard way into the hosted state for the checks above.</summary>
    private static void PossessFreshHost(World.World world)
    {
        world.Enemies.Add(new Entities.EnemyTank(world.Player.Position, elite: false));
        StepWithoutInput(world);
    }

    // --- The skyline over the wire ------------------------------------------------

    /// <summary>
    /// The last parity gap where two players genuinely saw different worlds. The city's
    /// layout was always identical everywhere — it comes off a fixed seed — but nothing
    /// carried the <em>damage</em>, so a tower one player cut down with a beam still stood on
    /// everybody else's screen, complete with its collision.
    /// </summary>
    private static string? StructureDamageCrossesTheWire()
    {
        var host = new World.World(new Loadout { Class = PlayerClass.Spider })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        var tower = FirstTower(host);
        if (tower == null) return "no tower to cut";
        if (host.Player.Spider == null) return "the spider chassis has no emitter";

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;

        Structure? mirror = ByIndex(client, tower.Index);
        if (mirror == null) return "the client's city does not have the same tower in it";
        if (mirror.Fracture != null) return "the client's tower started out already cut";

        // Stand off the tower and put a full lance through its base.
        Vector2 away = Vector2.Normalize(Torus.Delta(tower.Position, host.Player.Position)) * 20f;
        host.Player.Position = Torus.Wrap(tower.Position + away);
        host.Player.Heading = MathF.Atan2(-away.X, -away.Y);
        client.Players[0].Position = host.Player.Position;   // in range to be told about it
        for (int i = 0; i < 200; i++) host.Player.Spider!.Hold((float)Config.FixedDt);
        host.FireSpiderLanceForTest();
        if (!tower.Falling) return "the lance left the tower standing on the host";

        var buf = new byte[Snapshot.MaxStructureSize];
        int n = Snapshot.WriteStructures(host, forSeat: 0, tick: 5u, buf);
        Snapshot.ApplyStructures(client, buf.AsSpan(0, n));

        if (!mirror.Falling)
            return "the tower came down on the host and stood untouched on the client";
        if (mirror.Fracture == null)
            return "the client never built the chunk model the host had carved";

        // And the standing shape has to match, not merely the fact of the hit — otherwise
        // the client draws a tower with a hole in a different place from the one that is
        // actually there.
        if (StandingCells(mirror) != StandingCells(tower))
            return $"the client has {StandingCells(mirror)} cells standing, the host {StandingCells(tower)}";
        return null;
    }

    /// <summary>
    /// The keep-last packet has to keep saying so. A player who was on the far side of the
    /// world when a tower fell has never heard about it, and there is no second chance —
    /// the razed lot has to stay in the outgoing set or they drive into an invisible ruin.
    /// </summary>
    private static string? RazedLotsReachALateClient()
    {
        var host = new World.World(new Loadout { Class = PlayerClass.Spider })
        { DynamicSpawning = false };
        host.Enemies.Clear();
        var tower = FirstTower(host);
        if (tower == null) return "no tower to cut";

        Vector2 away = Vector2.Normalize(Torus.Delta(tower.Position, host.Player.Position)) * 20f;
        host.Player.Position = Torus.Wrap(tower.Position + away);
        host.Player.Heading = MathF.Atan2(-away.X, -away.Y);
        for (int i = 0; i < 200; i++) host.Player.Spider!.Hold((float)Config.FixedDt);
        host.FireSpiderLanceForTest();

        // Run the collapse right out, so the lot is cleared and the structure has left the
        // live field entirely.
        for (int i = 0; i < 60 * 6; i++) StepWithoutInput(host);
        if (host.Structures.Contains(tower)) return "the wreck never left the host's field";

        // Only now does the client come into range and get told.
        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        client.Players[0].Position = host.Player.Position;

        var buf = new byte[Snapshot.MaxStructureSize];
        int n = Snapshot.WriteStructures(host, forSeat: 0, tick: 9u, buf);
        Snapshot.ApplyStructures(client, buf.AsSpan(0, n));
        for (int i = 0; i < 60 * 6; i++) StepWithoutInput(client);

        if (ByIndex(client, tower.Index) is { Gone: false })
            return "a lot razed on the host was still standing on the client";
        if (client.Structures.Contains(ByIndex(client, tower.Index)!))
            return "the client never swept the cleared lot off its field";
        return null;
    }

    private static Structure? ByIndex(World.World world, int index)
    {
        foreach (var s in world.Structures) if (s.Index == index) return s;
        return null;
    }

    private static int StandingCells(Structure s)
    {
        if (s.Fracture is not { } f) return -1;
        int n = 0;
        foreach (var c in f.Chunks) if (c.Alive) n++;
        return n;
    }

    // --- Personal salvage ---------------------------------------------------------

    /// <summary>
    /// Collection used to test the local seat and only the local seat, and to pour whatever
    /// it found into one pack the whole world shared. In a match that meant salvage was
    /// unreachable for nineteen of the twenty players and, when it was reached, belonged to
    /// nobody in particular.
    /// </summary>
    private static string? SalvageIsPerSeat()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Pickups.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1

        // A cell parked on top of seat 1, well clear of seat 0.
        world.Players[0].Position = Torus.Wrap(new Vector2(-60f, 0f));
        world.Players[1].Position = Torus.Wrap(new Vector2(60f, 0f));
        world.Pickups.Add(new Pickup(world.Players[1].Position, PickupKind.Battery));

        StepWithoutInput(world);

        if (CountItems(world.InventoryOf(1), ItemKind.Battery) < 1)
            return "the craft standing on the cell did not pick it up";
        if (CountItems(world.InventoryOf(0), ItemKind.Battery) != 0)
            return "another seat's salvage landed in the host's own pack";
        return null;
    }

    /// <summary>
    /// A client's pack is a mirror, not the truth. It scribbles on it for an instant response
    /// and sends the host an intent; the host replays it against the real pack with the same
    /// rules and mirrors the result back.
    /// </summary>
    private static string? InventoryIntentsAreHostAuthoritative()
    {
        var (net, host, client, hw, cw) = SeatOne(4, "ACE");
        if (client.LocalSeat != 1) return $"the client seated at {client.LocalSeat}, not 1";

        // The host stocks the client's seat with something to move.
        Inventory pack = hw.InventoryOf(1);
        pack.Add(ItemKind.CrabFragment, 3);
        for (int i = 0; i < 10; i++) { net.Advance(); host.Pump(default); client.Pump(default); }

        if (CountItems(cw.InventoryOf(1), ItemKind.CrabFragment) != 3)
            return "the host's pack never reached the client";

        // The client moves one fragment from the grid into a crafting corner. It files the
        // intent exactly as the panel does.
        cw.Authoritative = false;
        cw.InventoryOf(1).Move(InvRegion.Slots, 0, InvRegion.Craft, 0, 1);
        cw.FileInvIntent(new InvIntent(InvOp.Move, InvRegion.Slots, 0, InvRegion.Craft, 0, 1));
        for (int i = 0; i < 10; i++) { net.Advance(); host.Pump(default); client.Pump(default); }

        if (pack.Craft[0].IsEmpty || pack.Craft[0].Kind != ItemKind.CrabFragment)
            return "the client's move never reached the host's pack";
        // Counted across the whole pack, not just the grid: the point of the move is that a
        // fragment left one and arrived in the other, and neither end may mint or drop one.
        if (CountEverywhere(pack, ItemKind.CrabFragment) != 3)
            return "the host's replay of the move invented or lost a fragment";

        // And a move the host refuses is undone by the echo rather than standing on the
        // client: a fragment cannot live in an equip slot, whatever the client claims.
        cw.InventoryOf(1).Slots[0] = new ItemStack(ItemKind.CrabFragment, 9);
        cw.InventoryOf(1).Weapons[0] = new ItemStack(ItemKind.CrabFragment, 9);
        cw.FileInvIntent(new InvIntent(InvOp.Move, InvRegion.Slots, 0, InvRegion.Weapons, 0, 9));
        for (int i = 0; i < 10; i++) { net.Advance(); host.Pump(default); client.Pump(default); }

        if (!pack.Weapons[0].IsEmpty)
            return "the host accepted a fragment into an equip slot";
        if (!cw.InventoryOf(1).Weapons[0].IsEmpty)
            return "the client's invented equip was never corrected by the host's echo";
        return null;
    }

    /// <summary>
    /// A teardown is the one inventory action with a die roll in it, so it is the one a client
    /// genuinely cannot get right on its own: it rolls its own metal for an instant answer and
    /// has to end up holding whatever the host rolled instead.
    /// </summary>
    private static string? BreakingIsTheHostsRoll()
    {
        var (net, host, client, hw, cw) = SeatOne(4, "ACE");
        if (client.LocalSeat != 1) return $"the client seated at {client.LocalSeat}, not 1";

        Inventory pack = hw.InventoryOf(1);
        pack.Break[0] = new ItemStack(ItemKind.Battery, 2);
        for (int i = 0; i < 10; i++) { net.Advance(); host.Pump(default); client.Pump(default); }
        if (cw.InventoryOf(1).Break[0].Count != 2)
            return "the host's bench never reached the client";

        // The client opens one, exactly as the panel does: it scribbles its own roll onto the
        // mirror and files the intent.
        cw.Authoritative = false;
        cw.InventoryOf(1).BreakOne();
        cw.FileInvIntent(new InvIntent(InvOp.Break, InvRegion.Break, 0, InvRegion.Parts, 0, 1));
        for (int i = 0; i < 20; i++) { net.Advance(); host.Pump(default); client.Pump(default); }

        if (pack.Break[0].Count != 1) return "the client's teardown never reached the host's bench";
        if (CountEverywhere(pack, ItemKind.CopperWire) != 1
            || CountEverywhere(pack, ItemKind.ScrapMetal) != 1)
            return "the host's replay of the teardown did not yield the certain parts";
        // And the mirror is the host's pack, roll and all — not the client's guess at it.
        if (cw.InventoryOf(1).Fingerprint() != pack.Fingerprint())
            return "the client kept its own roll instead of the host's";
        return null;
    }

    /// <summary>
    /// Shift + right-click on a stack: one unit leaves the pack and is lying on the grid in
    /// front of the craft — near enough to walk back to, far enough that the very next tick
    /// does not hand it straight back. And it is worth exactly what was thrown: one round out
    /// is one round back, not the handful a stray pickup off the field carries.
    /// </summary>
    private static string? ThrowingOneLandsItOnTheField()
    {
        var world = new World.World(new Loadout { Class = PlayerClass.Tank })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Pickups.Clear();

        Inventory pack = world.Inventory;
        pack.Slots[0] = new ItemStack(ItemKind.Bullet, 7);

        if (!world.ThrowOne(world.LocalIndex, InvRegion.Slots, 0))
            return "the throw was refused on a slot holding seven rounds";
        if (pack.Slots[0].Count != 6)
            return $"the throw took {7 - pack.Slots[0].Count} rounds off the stack, not one";
        if (world.Pickups.Count != 1)
            return $"{world.Pickups.Count} pieces of salvage landed for one thrown round";
        if (world.Pickups[0].Kind != PickupKind.Ammo)
            return "the thrown round landed as something else";
        if (world.Pickups[0].Amount != 1)
            return $"the thrown round is worth {world.Pickups[0].Amount} coming back";

        // Out past the collect reach, so it does not bounce straight back into the pack.
        float reach = PlayerTank.Radius + Pickup.Radius;
        if (Torus.Distance(world.Pickups[0].Position, world.Player.Position) <= reach)
            return "the thrown round landed inside the craft's own pickup radius";

        // A tick with nobody moving leaves it where it fell.
        StepWithoutInput(world);
        if (world.Pickups.Count != 1 || pack.Slots[0].Count != 6)
            return "the thrown round was scooped straight back up";

        // Driven over, it comes back as one round and is spent — a thrown item is not ambient
        // drift, so it must not reappear out in the fog and start printing rounds.
        world.Player.Position = world.Pickups[0].Position;
        StepWithoutInput(world);
        if (CountItems(pack, ItemKind.Bullet) != 7)
            return $"picking the thrown round back up gave {CountItems(pack, ItemKind.Bullet) - 6}";
        if (world.Pickups.Count != 0)
            return "the thrown round respawned out in the fog instead of being spent";

        // Every kind a pack can hold can be thrown, and each survives the round trip as itself.
        foreach (ItemKind kind in Enum.GetValues<ItemKind>())
            if (World.World.ItemOf(World.World.SalvageOf(kind)) != kind)
                return $"{ItemNames.Of(kind)} came back off the grid as something else";
        return null;
    }

    /// <summary>
    /// A client throwing an item away empties its own slot at once, but does not get to say
    /// where the item lands — salvage lies on the host's field or nowhere. The host replays the
    /// throw against the real pack and puts the item on the grid; the echo settles the client.
    /// </summary>
    private static string? ThrowingIsHostAuthoritative()
    {
        var (net, host, client, hw, cw) = SeatOne(4, "ACE");
        if (client.LocalSeat != 1) return $"the client seated at {client.LocalSeat}, not 1";
        hw.Pickups.Clear();

        Inventory pack = hw.InventoryOf(1);
        pack.Add(ItemKind.CrabFragment, 3);
        for (int i = 0; i < 10; i++) { net.Advance(); host.Pump(default); client.Pump(default); }
        if (CountItems(cw.InventoryOf(1), ItemKind.CrabFragment) != 3)
            return "the host's pack never reached the client";

        // The client throws one, exactly as the panel does.
        cw.Authoritative = false;
        int before = cw.Pickups.Count;
        if (!cw.ThrowOne(cw.LocalIndex, InvRegion.Slots, 0))
            return "the client's mirror refused the throw";
        if (CountEverywhere(cw.InventoryOf(1), ItemKind.CrabFragment) != 2)
            return "the client's mirror did not answer the throw at once";
        if (cw.Pickups.Count != before)
            return "the client invented a piece of salvage the host never placed";

        cw.FileInvIntent(new InvIntent(InvOp.Throw, InvRegion.Slots, 0, InvRegion.None, 0, 1));
        for (int i = 0; i < 20; i++) { net.Advance(); host.Pump(default); client.Pump(default); }

        if (CountEverywhere(pack, ItemKind.CrabFragment) != 2)
            return "the client's throw never reached the host's pack";
        if (hw.Pickups.Count != 1 || hw.Pickups[0].Kind != PickupKind.CrabFragment)
            return "the host never laid the thrown fragment on the field";
        // Thrown by seat 1, so it lies by seat 1's craft — not at the host's feet.
        if (Torus.Distance(hw.Pickups[0].Position, hw.Players[1].Position) > 6f)
            return "the thrown fragment landed somewhere other than by the craft that threw it";
        if (cw.InventoryOf(1).Fingerprint() != pack.Fingerprint())
            return "the host's echo and the client's mirror disagree after a throw";
        // And the salvage the host placed is drawn on the client that threw it.
        if (cw.Pickups.Count != 1 || cw.Pickups[0].Kind != PickupKind.CrabFragment)
            return "the thrown fragment never came back down the wire to be seen";
        return null;
    }

    private static string? InventoryCrossesTheWire()
    {
        var pack = new Inventory();
        pack.Add(ItemKind.Battery, 3);
        pack.Add(ItemKind.Bullet, 17);
        pack.Craft[1] = new ItemStack(ItemKind.CrabFragment, 1);
        pack.Weapons[2] = new ItemStack(ItemKind.CrabCore, 1);
        pack.Break[0] = new ItemStack(ItemKind.Battery, 2);
        pack.Parts[2] = new ItemStack(ItemKind.Lithium, 1);

        var buf = new byte[Inventory.WireSize];
        pack.Write(buf);
        var landed = new Inventory();
        landed.Read(buf);

        if (landed.Fingerprint() != pack.Fingerprint())
            return "a pack came off the wire different from the one that went on";
        if (landed.Weapons[2].Kind != ItemKind.CrabCore) return "the equip row did not survive";
        if (landed.Craft[1].Kind != ItemKind.CrabFragment) return "the craft corners did not survive";
        if (landed.Break[0].Count != 2) return "the take-apart bench did not survive";
        if (landed.Parts[2].Kind != ItemKind.Lithium) return "the parts row did not survive";

        // And the digest has to actually notice a change, or the host would never re-send.
        landed.Slots[0].Count++;
        if (landed.Fingerprint() == pack.Fingerprint())
            return "the digest did not notice a slot changing";
        return null;
    }

    // --- Spectating ---------------------------------------------------------------

    /// <summary>
    /// The revive model sends a spent player to watch the survivors, and until now nothing
    /// pointed the camera at one — a dead client sat staring out of its own wreck while the
    /// match carried on without it.
    /// </summary>
    private static string? SpentPlayerSpectatesASurvivor()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 0 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 2
        world.LocalIndex = 0;

        StepWithoutInput(world);
        if (world.Spectating) return "a living player was put into spectator mode";
        if (world.ViewSeat != 0) return "a living player's camera left their own craft";

        // Seat 0 is spent. Seat 2 is nearer than seat 1, so it is the one to watch.
        world.Players[2].Position = Torus.Wrap(world.Players[0].Position + new Vector2(4f, 0f));
        world.Players[0].Shield = 0f;
        world.Players[0].Health = 0f;
        world.Players[0].Lives = 0;
        if (!world.Players[0].Spectating) return "the test failed to put the local player out";

        StepWithoutInput(world);
        if (!world.Spectating) return "a spent player was left staring out of their own wreck";
        if (world.ViewSeat != 2)
            return $"the camera went to seat {world.ViewSeat}, not the nearest survivor";
        if (!ReferenceEquals(world.Eye, world.Players[2])) return "Eye is not the watched craft";

        // Sticky: the camera must not cut between team-mates as they drive past each other.
        world.Players[1].Position = world.Players[0].Position;
        StepWithoutInput(world);
        if (world.ViewSeat != 2) return "the camera cut away from a perfectly alive team-mate";

        // But it does move on when the one it was watching is spent too.
        world.Players[2].Shield = 0f;
        world.Players[2].Health = 0f;
        world.Players[2].Lives = 0;
        StepWithoutInput(world);
        if (world.ViewSeat != 1) return "the camera stayed on a craft that had gone out";

        // And a revive puts the player straight back behind their own eyes.
        world.Players[0].Lives = 1;
        world.Players[0].Shield = 50f;
        StepWithoutInput(world);
        if (world.Spectating) return "a revived player was left spectating";
        return null;
    }

    // --- The transient combat light ------------------------------------------------

    /// <summary>
    /// Cables, stolen lance shafts and the spider's charged beam were all drawn from the
    /// local player's own rig and nowhere else, so a team-mate's fight threw no light at all
    /// on anyone else's screen.
    /// </summary>
    private static string? RigsCrossTheWire()
    {
        var host = new World.World(new Loadout { Class = PlayerClass.Spider },
            new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        if (host.AddPlayer(new Loadout { Class = PlayerClass.Virus }) == null)   // seat 1
            return "the match refused a second seat";

        // Seat 0 winds its lance; seat 1's mote throws a shaft of the stolen one.
        for (int i = 0; i < 100; i++) host.Player.Spider!.Hold((float)Config.FixedDt);
        PlayerTank mote = host.Players[1];
        mote.Position = Torus.Wrap(new Vector2(6f, 0f));
        mote.Virus!.AddShaft(new Vector3(6f, 2f, 0f), new Vector3(0f, 0f, 1f));

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.AddPlayer();
        client.LocalIndex = 1;   // we are the mote; seat 0's beam is somebody else's

        var buf = new byte[Snapshot.MaxRigSize];
        int n = Snapshot.WriteRigs(host, forSeat: 1, tick: 2u, buf);
        Snapshot.ApplyRigs(client, buf.AsSpan(0, n));

        if (!client.RemoteRigs.TryGetValue(0, out var beam))
            return "the spider's gathering lance never reached the client";
        if (!beam.HasBeam) return "the seat arrived with no beam on it";
        if (beam.BeamCharge < 0.2f)
            return $"the charge crossed at {beam.BeamCharge:0.00}, nothing like the meter it was";

        // Our own seat is drawn from its live rig, so the wire's copy of it must be thrown
        // away on arrival — keeping it would draw every shaft twice, at double brightness and
        // a round trip out of date.
        if (client.RemoteRigs.ContainsKey(1))
            return "the client kept the host's copy of its own rig and would draw it twice";

        // A shaft from a seat that is not ours does cross.
        var other = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        other.AddPlayer();
        other.LocalIndex = 0;
        int n2 = Snapshot.WriteRigs(host, forSeat: 0, tick: 3u, buf);
        Snapshot.ApplyRigs(other, buf.AsSpan(0, n2));
        if (!other.RemoteRigs.TryGetValue(1, out var stolen) || stolen.ShaftCount < 1)
            return "the virus's stolen lance threw no light on an onlooker's screen";
        if (stolen.Shafts[0].Life <= 0f) return "the shaft arrived already burnt out";
        return null;
    }

    // --- Lag compensation ----------------------------------------------------------

    /// <summary>
    /// The host decides hits against where everything is now; a client saw the field a round
    /// trip ago. At any real ping that gap is the whole of "I clearly hit them" missing. The
    /// host rewinds the world to what the shooter could actually see.
    /// </summary>
    private static string? LagCompensationRewindsTheTarget()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Pickups.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1, our laggy client

        var mark = new EnemyTank(Torus.Wrap(new Vector2(0f, 40f)), elite: false);
        world.Enemies.Add(mark);

        // Fill the history with the hunter parked at a known spot, then move it well clear.
        const int Lag = 12;   // 200 ms at 60 Hz
        Vector2 wasAt = mark.Position;
        for (int i = 0; i < Lag + 4; i++) world.StepForTest((float)Config.FixedDt);
        // The step drives the hunter, so pin it back where the history says it stood, take one
        // more frame of history there, and only then teleport it away.
        mark.Position = wasAt;
        world.StepForTest((float)Config.FixedDt);
        wasAt = mark.Position;
        for (int i = 0; i < Lag; i++)
        {
            mark.Position = Torus.Wrap(mark.Position + new Vector2(0f, 3f));
            world.StepForTest((float)Config.FixedDt);
        }

        // Uncompensated, the host's answer is wherever the hunter is now.
        if (Torus.Distance(world.Rewound(mark.HitId, 1, mark.Position), mark.Position) > 0.01f)
            return "a seat with no measured lag was rewound anyway";

        // Told this seat is running Lag ticks behind, the same question answers with where the
        // hunter stood on that client's screen.
        world.SetSeatLag(1, Lag);
        Vector2 seen = world.Rewound(mark.HitId, 1, mark.Position);
        if (Torus.Distance(seen, mark.Position) < 1f)
            return "the rewind handed back the live position, not the one the shooter saw";
        if (Torus.Distance(seen, wasAt) > 4f)
            return $"the rewind landed {Torus.Distance(seen, wasAt):0.0} from where the shooter saw it";

        // The host's own shots are never rewound — it is not behind itself.
        if (Torus.Distance(world.Rewound(mark.HitId, 0, mark.Position), mark.Position) > 0.01f)
            return "the host's own shot was rewound against its own world";

        // And a rewind further back than the history reaches falls through to the truth
        // rather than reaching for a frame that has rolled off.
        world.SetSeatLag(1, 39);
        if (Torus.Distance(world.Rewound(mark.HitId, 1, mark.Position), mark.Position) > 0.01f)
            return "a rewind past the end of the history invented a position";
        return null;
    }

    /// <summary>
    /// The same class of bug the virus chain had, in the soldier and fish kits. The host runs
    /// every seat's triggers, but the cable kit fired from <c>Player</c> — this machine's own
    /// craft — so a remote player's hook left the <em>host's</em> hip, their gas jump kicked
    /// dust up under the host, and their crosshair overwrote the host's own anchor bracket.
    /// </summary>
    private static string? RigTriggersAreSeatAware()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Soldier }) == null)   // seat 1
            return "the match refused a second seat";
        world.LocalIndex = 0;

        PlayerTank mate = world.Players[1];
        if (mate.Soldier is not { } rig) return "the soldier seat has no rig";

        // Stand them a long way from the host, facing a known way.
        mate.Position = Torus.Wrap(new Vector2(120f, -40f));
        mate.Heading = 0f;
        mate.Height = 0f;

        // A cable has to leave THEIR hip, not ours.
        Vector3 muzzle = world.SoldierMuzzle(mate, right: true);
        if (Torus.Distance(new Vector2(muzzle.X, muzzle.Z), mate.Position) > 2f)
            return "a seat's cable muzzle is nowhere near that seat's body";
        if (Torus.Distance(new Vector2(muzzle.X, muzzle.Z), world.Players[0].Position) < 20f)
            return "a remote seat's cable muzzle sits on the host's own craft";

        // Drive their hook from their own input frame, as the host does for a wire packet.
        var fire = new InputFrame(Btn.RightHook, Btn.RightHook, Vector2.Zero);
        world.SetInput(1, fire);
        world.Update((float)Config.FixedDt, InputFrame.Empty);

        if (!rig.Right.Out) return "the remote player's hook never left the launcher";
        if (world.Players[0].Soldier is { Right.Out: true })
            return "the remote player's press fired the host's own hook";

        // And their aim must not have stolen this machine's crosshair readout, which is a
        // fact about the one pair of eyes at this keyboard.
        if (world.AnchorInSight != null)
            return "a remote player's aim wrote the local crosshair's anchor bracket";
        return null;
    }

    /// <summary>
    /// The same leak again, in the last two kits: the TANK's dischargers and the SPIDER's
    /// claw and legs. Both trigger functions took the acting seat, then called helpers that
    /// read <c>Player</c> — so on the host a remote tank's smoke screen appeared around the
    /// HOST's craft, a remote spider's throw flew off along the host's heading, and its
    /// pounce launched the host up a wall.
    /// </summary>
    private static string? MachineKitsAreSeatAware()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Smoke.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)   // seat 1
            return "the match refused a second seat";
        world.LocalIndex = 0;

        PlayerTank host = world.Players[0];
        PlayerTank mate = world.Players[1];
        host.Position = Torus.Wrap(new Vector2(-90f, 0f));
        mate.Position = Torus.Wrap(new Vector2(90f, 0f));
        mate.Heading = 0f;

        // Seat 1 vents its dischargers, from its own input frame as a wire packet would.
        world.SetInput(1, new InputFrame(Btn.TankSmoke, Btn.TankSmoke, Vector2.Zero));
        world.Update((float)Config.FixedDt, InputFrame.Empty);

        if (world.Smoke.Count == 0) return "the remote tank's dischargers never fired";
        foreach (var cloud in world.Smoke)
        {
            if (Torus.Distance(cloud.Position, mate.Position) > 12f)
                return "the remote tank's screen was laid somewhere other than on that tank";
            if (Torus.Distance(cloud.Position, host.Position) < 30f)
                return "a remote tank's smoke screen was laid around the host's own craft";
        }

        // And the SPIDER's claw: the grab has to reach from ITS craft, not from the host's.
        var spiderWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        spiderWorld.Enemies.Clear();
        if (spiderWorld.AddPlayer(new Loadout { Class = PlayerClass.Spider }) == null)
            return "the match refused a spider seat";
        spiderWorld.LocalIndex = 0;

        PlayerTank arachnid = spiderWorld.Players[1];
        arachnid.Position = Torus.Wrap(new Vector2(90f, 0f));
        spiderWorld.Players[0].Position = Torus.Wrap(new Vector2(-90f, 0f));

        // A hunter in arm's reach of the SPIDER and nowhere near the host.
        var prey = new EnemyTank(Torus.Wrap(arachnid.Position + new Vector2(2f, 0f)), elite: false);
        spiderWorld.Enemies.Add(prey);

        spiderWorld.SetInput(1, new InputFrame(Btn.Fire, Btn.Fire, Vector2.Zero));
        spiderWorld.Update((float)Config.FixedDt, InputFrame.Empty);

        if (arachnid.Claw is not { Holding: true } claw)
            return "the remote spider's claw never closed on a hunter in its own reach";
        if (!ReferenceEquals(claw.Victim, prey))
            return "the remote spider grabbed something other than the hunter beside it";

        // And the TANK's ram, which read Player throughout — so on the host only the host's
        // own hull could crush anything and a remote player drove through hunters untouched.
        var ramWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        ramWorld.Enemies.Clear();
        if (ramWorld.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a ramming seat";
        ramWorld.LocalIndex = 0;
        ramWorld.Players[0].Position = Torus.Wrap(new Vector2(-90f, 0f));

        PlayerTank rammer = ramWorld.Players[1];
        rammer.Position = Torus.Wrap(new Vector2(90f, 0f));
        rammer.Heading = 0f;

        // Wind the hull up to ramming speed under its own drive, then park a hunter on its nose.
        var forward = new InputFrame(Btn.Forward, Btn.None, Vector2.Zero);
        for (int i = 0; i < 120; i++)
        {
            ramWorld.SetInput(1, forward);
            ramWorld.Update((float)Config.FixedDt, InputFrame.Empty);
        }
        if (rammer.DriveVelocity.Length() < rammer.RamThreshold)
            return "the remote tank never reached ramming speed";

        var run = new EnemyTank(Torus.Wrap(rammer.Position + rammer.Forward * 1.5f), elite: false);
        float wasShield = run.Shield;
        ramWorld.Enemies.Add(run);
        ramWorld.SetInput(1, forward);
        ramWorld.Update((float)Config.FixedDt, InputFrame.Empty);

        if (run.Alive && run.Shield >= wasShield)
            return "a remote tank drove straight through a hunter without touching it";
        return null;
    }

    // --- The rest of the sim, converted to seats ----------------------------------

    /// <summary>
    /// The wall pass ran on the local craft alone, so on the host every REMOTE player drove
    /// clean through the city — and the host's own snapshots then placed them inside towers
    /// on everybody's screen.
    /// </summary>
    private static string? WallsAreSolidForEverySeat()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a second seat";
        world.LocalIndex = 0;

        var tower = FirstTower(world);
        if (tower == null) return "no tower to drive into";

        // Park the host far away and shove seat 1 into the middle of a tower's footprint.
        world.Players[0].Position = Torus.Wrap(tower.Position + new Vector2(150f, 0f));
        PlayerTank mate = world.Players[1];
        mate.Position = tower.Position;
        mate.Height = 0f;

        StepWithoutInput(world);

        // It has to have been pushed clear of the footprint, whatever the footprint is.
        if (Torus.Distance(mate.Position, tower.Position) < PlayerTank.Radius)
            return "a remote craft was left standing inside a tower";
        return null;
    }

    /// <summary>
    /// Squads flew at <c>Player.Position</c> flat out, so on the host a squad only ever
    /// hunted the host — nineteen other players could stand in the open and never be hunted,
    /// shot at or bladed.
    /// </summary>
    private static string? SquadsHuntEverySeat()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a second seat";
        world.LocalIndex = 0;

        // Opposite corners of the torus, and a squad next to seat 1. Corners rather than
        // opposite edges: the world wraps at 400, so (-150,0) and (150,0) are a hundred units
        // apart the short way round, not three hundred.
        world.Players[0].Position = Torus.Wrap(new Vector2(0f, 0f));
        PlayerTank mate = world.Players[1];
        mate.Position = Torus.Wrap(new Vector2(195f, 195f));

        // Raised well inside their own alert range of the mate and far outside it of the
        // host, so "did they wake up?" is the whole question. A squad that only ever measures
        // itself against the local seat sits on its tower and watches, forever.
        world.SpawnSoldierSquad(Torus.Wrap(mate.Position + new Vector2(0f, 40f)));
        if (world.Squads.Count == 0 || world.Soldiers.Count == 0) return "no squad was raised";

        Vector2 startedAt = world.Soldiers[0].Position;
        float toMate = Torus.Distance(startedAt, mate.Position);
        float toHost = Torus.Distance(startedAt, world.Players[0].Position);
        if (toMate > 120f) return $"the test raised the squad {toMate:0} from the mate, out of alert range";
        if (toHost < 200f) return $"the test raised the squad only {toHost:0} from the host";

        for (int i = 0; i < 60 * 10; i++) StepWithoutInput(world);
        if (world.Squads.Count == 0) return "the squad was gone before it could be measured";

        // Waking up at all is the thing: alertness is measured against whoever they decided
        // their target is, and the only craft in range is the one that is not this machine's.
        if (!world.Squads[0].Alerted)
            return "a squad sat on its tower while a player stood in the open beside it";

        // And they have to have actually come for them.
        float now = float.MaxValue;
        foreach (var s in world.Soldiers)
            now = MathF.Min(now, Torus.Distance(s.Position, mate.Position));
        if (now >= toMate)
            return $"the squad never closed on the nearest player ({toMate:0} -> {now:0})";
        return null;
    }

    /// <summary>Falling rubble billed the local craft alone — so the one thing the whole
    /// destruction system exists for could only ever happen to one of the twenty people
    /// standing under a collapse.</summary>
    private static string? CrushBillsEverySeat()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a second seat";
        world.LocalIndex = 0;

        // A clear patch well away from the origin and from any tower's footprint, with the
        // host nowhere near it — so nothing but the remote player can be billed.
        var mark = new Vector2(30f, 12f);
        world.Players[0].Position = Torus.Wrap(new Vector2(-150f, -150f));
        PlayerTank mate = world.Players[1];
        mate.Position = mark;
        mate.Height = 0f;
        float wasShield = mate.Shield;

        // Steer one mass-bearing chunk straight down onto them at a section's speed — the
        // exact moment a piece of a felled tower arrives on somebody underneath it. Driven
        // directly rather than by felling a real building, so the test measures the billing
        // and not the scatter of a particular tower's collapse.
        world.Debris.Rubble(new Vector3(mark.X, 6f, mark.Y), Palette.StructureShell,
            chunks: 8, scale: 2f);

        var shards = world.Debris.Shards;
        bool placed = false;
        for (int i = 0; i < shards.Length; i++)
        {
            if (!shards[i].Active || shards[i].Mass <= 0f) continue;
            shards[i].Position = new Vector3(mark.X, 1.5f, mark.Y);
            shards[i].Velocity = new Vector3(0f, -14f, 0f);
            placed = true;
            break;
        }
        if (!placed) return "no mass-bearing rubble was spawned to test the crush";

        StepWithoutInput(world);

        if (mate.Shield >= wasShield && mate.Alive)
            return "a section came down on a remote player and they walked away";
        if (world.Players[0].Shield < world.Players[0].MaxShield)
            return "the chunk billed the host, who was on the other side of the world";
        return null;
    }

    /// <summary>The whole spawn director hung off <c>Player</c>, so the field was built
    /// around the host and a client who drove away found an empty world.</summary>
    private static string? SpawnsFollowEverySeat()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4 });
        world.Enemies.Clear();
        world.Pickups.Clear();
        if (world.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a second seat";
        world.LocalIndex = 0;

        // Two players as near to opposite corners of the torus as it has. The separation is
        // chosen against the spawn ring deliberately: nothing dropped around the host can
        // land within Near of the mate, so anything that does is unambiguously theirs. A
        // plain "nearer to one than the other" test would be decided by the wrap.
        world.Players[0].Position = Torus.Wrap(new Vector2(0f, 0f));
        PlayerTank mate = world.Players[1];
        mate.Position = Torus.Wrap(new Vector2(195f, 195f));
        const float Near = 130f;   // just past the ring's outer edge (SpawnMaxRange is 120)

        // Counted rather than merely spotted: with the anchor rolled per spawn, roughly half
        // of everything the director produces belongs to each player, so a healthy share near
        // the mate is the signal. One stray sighting would not be — a hunter chases whoever
        // is nearest it and could wander.
        int mateSpawns = 0;
        var seen = new HashSet<object>();
        for (int i = 0; i < 60 * 90; i++)
        {
            StepWithoutInput(world);
            // Score each thing once, at the moment it first appears, so a chaser that drifts
            // across the map later cannot be mistaken for something that spawned there.
            foreach (var e in world.Enemies)
                if (seen.Add(e) && Torus.Distance(e.Position, mate.Position) < Near) mateSpawns++;
            foreach (var pk in world.Pickups)
                if (seen.Add(pk) && Torus.Distance(pk.Position, mate.Position) < Near) mateSpawns++;
        }

        if (mateSpawns < 3)
            return $"ninety seconds of spawning put {mateSpawns} things near the second player";
        return null;
    }

    // --- helpers: advance the sim without going through global input ---

    private static void StepWithoutInput(World.World world)
    {
        // Re-implements World.Update minus the InputMap read, so the test needs
        // no Raylib window. Player fire is injected explicitly by the caller.
        world.StepForTest((float)Config.FixedDt);
    }

    private static void AimPlayerAtFirstEnemy(World.World world)
    {
        if (world.Enemies.Count == 0) return;
        Vector2 to = world.Enemies[0].Position - world.Player.Position;
        world.Player.Heading = MathF.Atan2(to.X, to.Y);
    }

    // --- The monsters can reach anybody, not just seat 0 --------------------------
    //
    // Every one of these was broken the same way and for the same reason: the boss attacks
    // were written when `World.Player` meant "the player, there is only one", and they were
    // never converted. On a host that is seat 0 — itself — so in a five-player match the
    // Crab-Core's beam, its claw, the Maw-Core's lasers and its throat could only ever touch
    // the person hosting. Everyone else walked through a firing boss untouched.

    /// <summary>Two seats, and the world stepped without any input. Seat 0 is the local one,
    /// as it is on a host.</summary>
    private static World.World TwoSeatWorld(bool friendlyFire = false)
    {
        var world = new World.World(null,
            new MatchSettings { MaxPlayers = 4, FriendlyFire = friendlyFire })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });
        world.LocalIndex = 0;
        // Opposite corners, not opposite edges — the torus wraps at 400, so (-150,0) and
        // (150,0) are a hundred units apart the short way round rather than three hundred.
        world.Players[0].Position = Torus.Wrap(new Vector2(0f, 0f));
        world.Players[1].Position = Torus.Wrap(new Vector2(195f, 195f));
        return world;
    }

    private static string? BeamBurnsEverySeat()
    {
        World.World world = TwoSeatWorld();
        PlayerTank mate = world.Players[1];

        // Raise the boss beside the mate, far from the host, and let it run its protocol
        // until it fires. The beam locks its direction at the craft it aimed at.
        world.SpawnCrabAt(Torus.Wrap(mate.Position + new Vector2(18f, 0f)));
        if (world.Boss is null) return "no boss was raised";

        float before = mate.Shield;
        float hostBefore = world.Players[0].Shield;
        for (int i = 0; i < 60 * 30 && mate.Shield >= before; i++) StepWithoutInput(world);

        if (mate.Shield >= before)
            return "a Crab-Core stood next to a player for thirty seconds and never hurt them";
        if (world.Players[0].Shield < hostBefore)
            return "the boss hurt the host, who was on the far side of the world";
        return null;
    }

    private static string? SeizureTakesEverySeat()
    {
        World.World world = TwoSeatWorld();
        PlayerTank mate = world.Players[1];
        world.SpawnCrabAt(Torus.Wrap(mate.Position + new Vector2(6f, 0f)));
        if (world.Boss is null) return "no boss was raised";

        for (int i = 0; i < 60 * 40 && world.Seizure is null; i++) StepWithoutInput(world);

        if (world.Seizure is not { } grab)
            return "a Crab-Core cornered a player for forty seconds and never picked them up";
        if (!ReferenceEquals(grab.Victim, mate))
            return "the boss grabbed the host, who was on the far side of the world";
        if (!mate.Captured) return "the seized craft was never marked captured";
        // And the camera effect belongs to the person in the claw, not to everyone.
        if (world.Cinematic != null)
            return "a team-mate's seizure took over the local player's camera";
        return null;
    }

    private static string? MawSwallowsEverySeat()
    {
        World.World world = TwoSeatWorld();
        PlayerTank mate = world.Players[1];
        world.SpawnMawAt(Torus.Wrap(mate.Position));
        if (world.Maw is null) return "no maw was raised";

        for (int i = 0; i < 60 * 40 && world.Digestion is null; i++) StepWithoutInput(world);

        if (world.Digestion is not { } meal)
            return "a Maw-Core hung over a still player for forty seconds and never ate them";
        if (!ReferenceEquals(meal.Victim, mate))
            return "the mouth swallowed the host, who was on the far side of the world";
        if (world.Cinematic != null)
            return "a team-mate's digestion took over the local player's camera";
        return null;
    }

    private static string? MawLasersBiteEverySeat()
    {
        World.World world = TwoSeatWorld();
        PlayerTank mate = world.Players[1];
        // Off to one side, so the mouth shoots at them rather than swallowing them: a
        // digestion would mask whether the lasers themselves ever reach a remote seat.
        mate.Position = Torus.Wrap(new Vector2(195f, 175f));
        world.SpawnMawAt(Torus.Wrap(new Vector2(195f, 195f)));
        if (world.Maw is null) return "no maw was raised";

        float before = mate.Shield;
        PlayerTank host = world.Players[0];
        float hostBefore = host.Shield;

        // The mouth decides for itself whether to shoot or lunge, and both are the same
        // question here — does it engage a seat that is not this machine's? So the run ends
        // as soon as it does either, and the assertion is that the thing it reached was the
        // mate. Written this way rather than waiting only on laser damage, which the AI can
        // pre-empt with a swallow and leave the check passing without having tested anything.
        bool reached = false;
        for (int i = 0; i < 60 * 40 && !reached; i++)
        {
            StepWithoutInput(world);
            reached = mate.Shield < before || world.Digestion != null;
        }

        if (!reached)
            return "a Maw-Core hung over a player for forty seconds and never engaged them";
        if (world.Digestion is { } d && !ReferenceEquals(d.Victim, mate))
            return "the mouth reached past the nearby player to swallow the distant host";
        if (host.Shield < hostBefore)
            return "the mouth hurt the host, who was on the far side of the world";
        return null;
    }

    /// <summary>
    /// The other half of making a seizure reach any seat: everything that used to freeze "the
    /// player" while a cinematic ran asked whether <em>anybody</em> was held. Left that way,
    /// one player getting grabbed would have locked all twenty players' triggers.
    /// </summary>
    private static string? ASeizedMateDoesNotFreezeTheRoom()
    {
        World.World world = TwoSeatWorld();
        PlayerTank host = world.Players[0];
        PlayerTank mate = world.Players[1];
        world.SpawnCrabAt(Torus.Wrap(mate.Position + new Vector2(6f, 0f)));
        if (world.Boss is null) return "no boss was raised";

        for (int i = 0; i < 60 * 40 && world.Seizure is null; i++) StepWithoutInput(world);
        if (world.Seizure is not { } grab) return "the boss never grabbed anybody";
        if (!ReferenceEquals(grab.Victim, mate)) return "the boss grabbed the wrong seat";

        // The host is nowhere near it and must be able to fight normally.
        int ammo = host.Ammo;
        world.FirePlayerShot(laser: false, by: host);
        if (host.Ammo == ammo)
            return "a team-mate being seized froze the trigger of a player across the world";

        bool anyRound = false;
        foreach (var p in world.Projectiles) if (p.Active && p.Owner == 0) anyRound = true;
        if (!anyRound) return "the shot cost ammo but no round left the barrel";
        return null;
    }

    /// <summary>
    /// A splash round reaches every seat standing in it — the bug this originally caught was a
    /// burst that only ever asked about seat 0, so a team-mate stood in the fireball unharmed —
    /// and it now also has to obey the two rules a burst was quietly ignoring: the host's
    /// friendly-fire toggle decides whether it reaches anybody else at all, and the craft that
    /// lobbed it always wears its own burst whatever the toggle says.
    /// </summary>
    private static string? SplashBitesEverySeat()
    {
        // Both craft standing on the same spot, and seat 0 drops a mortar on their own feet.
        static (float Mate, float Thrower) LobOnBothHeads(bool friendlyFire)
        {
            World.World world = TwoSeatWorld(friendlyFire);
            PlayerTank thrower = world.Players[0];
            PlayerTank mate = world.Players[1];
            thrower.Position = Torus.Wrap(new Vector2(0f, 0f));
            mate.Position = Torus.Wrap(new Vector2(1.5f, 0f));

            float mateBefore = mate.Shield, throwerBefore = thrower.Shield;
            world.DetonateMortarForTest(mate.Position, by: 0);
            return (mateBefore - mate.Shield, throwerBefore - thrower.Shield);
        }

        var on = LobOnBothHeads(friendlyFire: true);
        if (on.Mate <= 0.001f) return "a mortar burst on a player's head and they did not feel it";
        if (on.Thrower <= 0.001f) return "a player dropped a mortar on their own feet for free";

        var off = LobOnBothHeads(friendlyFire: false);
        if (off.Mate > 0.001f)
            return $"friendly fire was off and a mortar still cost a team-mate {off.Mate:0.0} shield";
        if (off.Thrower <= 0.001f)
            return "friendly fire off spared a player from their own burst, which is not what it is for";
        return null;
    }

    // --- A room with more than two people in it -----------------------------------
    //
    // Every netcode test above this point drives a host and exactly one client, which is the
    // one arrangement that mostly worked. Almost everything that was actually wrong with this
    // game's multiplayer only shows up with a third person in the room, or with somebody
    // walking out of it, or with somebody arriving after everyone else had settled.
    //
    // Rather than hand-rolling the handshake in each test, this stands up a whole session the
    // way the loop really does — Game.StartHosting, Game.StartJoining and Game.UpdateLobby,
    // step for step — so a test exercises the code that ships rather than a sketch of it.

    /// <summary>One machine in a test session: its transport, its session, its lobby room and
    /// its world, driven exactly as <see cref="Game"/> drives them.</summary>
    private sealed class Machine
    {
        public Session Net = null!;
        public World.LobbyRoom? Room;
        public World.World World = null!;
        public Loadout Build = null!;
        public string Name = "";
        public bool InMatch;
        public bool GoneAway;
    }

    /// <summary>A whole test session: one host and up to nineteen clients on a shared wire.</summary>
    private sealed class Session3Plus
    {
        public LoopbackNet Wire = null!;
        public Machine[] All = null!;
        public MatchSettings Rules = null!;

        public Machine Host => All[0];

        /// <summary>One frame everywhere: the wire ticks, then each machine runs whichever of
        /// the loop's two paths it is on (the lobby screen, or the live match).</summary>
        public void Step()
        {
            Wire.Advance();
            foreach (Machine m in All)
            {
                if (m.GoneAway || m.Net is null) continue;

                if (m.InMatch)
                {
                    m.Net.Pump(InputFrame.Empty);
                    m.World.Update((float)Config.FixedDt, InputFrame.Empty);
                    continue;
                }
                if (m.Room is null) continue;

                // --- Game.UpdateLobby ---
                if (m.Net.Rejected is { } no) { m.Room.Fail(no); continue; }
                if (!m.Net.IsHost && m.Net.LocalSeat >= 0
                    && m.Room.Stage != World.LobbyRoom.Phase.InRoom)
                    m.Room.Seat(m.Net.LocalSeat, m.Net.LocalName);
                if (m.Net.IsHost && m.Net.World is { } hw) hw.Match = m.Room.Match.Clamped();
                m.Room.Update(InputFrame.Empty, (float)Config.FixedDt);
                m.Net.LobbyTick(m.Room);

                // A client comes in on LAUNCH, but only once it has chosen a craft.
                if (!m.Net.IsHost && m.Net is { MatchStarted: true, LocalSeat: >= 0 }
                    && m.Room is { MyChassis: not null } r)
                {
                    World.World jw = m.Net.World!;
                    jw.ReplacePlayer(m.Net.LocalSeat, r.MyBuild);
                    foreach (var a in r.Avatars.Values)
                        if (!jw.SeatNames.ContainsKey(a.Seat)) jw.SeatNames[a.Seat] = a.Name;
                    m.Net.Room = null;
                    m.Room = null;
                    m.World = jw;
                    m.InMatch = true;
                }
            }
        }

        public void Step(int frames) { for (int i = 0; i < frames; i++) Step(); }

        /// <summary>Game.StartJoining: dials the host and waits to be seated.</summary>
        public void Join(int peer)
        {
            Machine m = All[peer];
            m.GoneAway = false;
            // A reconnection is a fresh socket the host can reach again, which is what a real
            // transport does for it; without this a rejoiner would get the host's directed
            // sends and none of its broadcasts.
            Wire.Readmit(0, peer);
            m.Build = new Loadout { Class = PlayerClass.Tank };
            m.World = new World.World(m.Build) { DynamicSpawning = false, Authoritative = false };
            m.Net = new Session(Wire[peer], host: false) { LocalName = m.Name };
            m.Net.JoinMatch(m.World);
            m.Net.SendHello(m.Build.Class);
            // The room's pod edits this machine's own long-lived build, exactly as the loop
            // hands Game._loadout to it — so a test can spend points on m.Build and expect
            // them to be what leaves on the wire.
            m.Room = new World.LobbyRoom(m.Build) { IsHost = false };
            m.Net.Room = m.Room;
            m.Room.Connecting();
        }

        /// <summary>Game.UpdateLobby's LAUNCH branch.</summary>
        public void Launch()
        {
            World.World hw = Host.Net.World!;
            foreach (var p in hw.Players) p.Lives = hw.Match.Revives + 1;
            Host.Net.StartMatch();
            Host.Net.Room = null;
            Host.Room = null;
            Host.InMatch = true;
        }

        /// <summary>Somebody's connection dies, from the host's point of view.</summary>
        public void Leave(int peer)
        {
            Wire.DropPeer(0, peer);
            All[peer].GoneAway = true;
        }
    }

    /// <summary>
    /// Stands up a host and room for <paramref name="clients"/> joiners, dials
    /// <paramref name="joinNow"/> of them in (all of them by default), and pumps until the
    /// handshake has settled. The rest are machines the wire knows about that have not picked
    /// up the phone yet — which is what a test of a late arrival needs.
    /// </summary>
    private static Session3Plus OpenRoom(int clients, MatchSettings rules,
        LinkQuality quality = default, int seed = 4242, int joinNow = -1)
    {
        if (joinNow < 0) joinNow = clients;
        var s = new Session3Plus
        {
            Wire = new LoopbackNet(clients + 1, quality, seed),
            All = new Machine[clients + 1],
            Rules = rules,
        };
        for (int i = 0; i <= clients; i++)
            s.All[i] = new Machine { Name = i == 0 ? "HOST" : $"PLAYER{i}" };

        // Game.StartHosting.
        Machine h = s.Host;
        h.Build = new Loadout { Class = PlayerClass.Tank };
        h.Room = new World.LobbyRoom(h.Build) { IsHost = true };
        h.Room.AdoptRules(rules);
        h.World = new World.World(h.Build, h.Room.Match.Clamped()) { DynamicSpawning = false };
        h.World.Enemies.Clear();
        h.Net = new Session(s.Wire[0], host: true) { LocalName = h.Name };
        h.Net.HostMatch(h.World);
        h.Net.Room = h.Room;
        h.Room.Seat(0, h.Name);

        for (int i = 1; i <= joinNow; i++) s.Join(i);
        s.Step(120);
        return s;
    }

    /// <summary>The whole point of the mode: five people pick five different craft and every
    /// machine — the host's included — shows every one of them as what its player chose. This
    /// is asserted twice, because they are two entirely separate paths: the lobby room carries
    /// a chassis in its own RoomState packet, and the match carries it in the players packet.</summary>
    private static string? EveryoneSeesEveryChassis()
    {
        var picks = new[] { PlayerClass.Spider, PlayerClass.Virus, PlayerClass.Fish,
                            PlayerClass.Soldier, PlayerClass.Tank };
        Session3Plus s = OpenRoom(4, new MatchSettings { MaxPlayers = 8, Revives = 2 },
            LinkQuality.Awful);

        // Four people dialling at once are seated in the order their hellos actually land,
        // which a jittery wire shuffles — so what each of them picked is keyed by the seat
        // they were really given, not by which machine in the test they happen to be.
        var wants = new PlayerClass[5];
        for (int i = 0; i < 5; i++)
        {
            int seat = s.All[i].Net.LocalSeat;
            if (seat < 0 || seat > 4) return $"player {i} was never seated (at {seat})";
            if (wants[seat] != default && seat != 0)
                return $"two players were handed seat {seat}";
            wants[seat] = picks[i];
            s.All[i].Room!.PickForTest(picks[i]);
            s.Step(20);
        }
        if (s.Host.Net.LocalSeat != 0) return "the host was not seat 0";
        s.Step(90);

        // The lobby floor: everyone's figure, on everyone's screen, as the craft they chose.
        for (int who = 0; who < 5; who++)
            for (int seat = 0; seat < 5; seat++)
            {
                if (!s.All[who].Room!.Avatars.TryGetValue(seat, out var a))
                    return $"player {who} could not see seat {seat} in the room at all";
                if (a.Chassis != wants[seat])
                    return $"player {who} saw seat {seat} in the room as " +
                           $"{a.Chassis?.ToString() ?? "nothing"}, not {wants[seat]}";
                if (!a.Ready) return $"player {who} saw seat {seat} as not ready after it picked";
            }
        if (!s.Host.Room!.AllReady) return "everybody had picked and the launch gate stayed shut";

        // ...and the match.
        s.Launch();
        s.Step(300);
        for (int who = 0; who < 5; who++)
        {
            World.World w = s.All[who].World;
            if (!s.All[who].InMatch) return $"player {who} never came in when the host launched";
            if (w.Players.Count != 5)
                return $"player {who} sees {w.Players.Count} craft in the match, not five";
            for (int seat = 0; seat < 5; seat++)
                if (w.Players[seat].Class != wants[seat])
                    return $"in the match, player {who} sees seat {seat} as " +
                           $"{w.Players[seat].Class}, not the {wants[seat]} they picked";
        }
        return null;
    }

    /// <summary>
    /// The renderer draws a craft from `PlayerTank.Build`, and every other check in this file
    /// asserts `PlayerTank.Class`. While `Build` was the loop's own long-lived `Loadout` held
    /// by reference — which is what `World`'s constructor handed seat 0 — those were two
    /// different facts, and only the one nobody was looking at reached the screen.
    ///
    /// The shape of the bug: a client's seat 0 is the HOST. It is built from the client's own
    /// loadout, and `ApplyPlayers` only rebuilds a seat when the class it is told differs from
    /// the class it has. So whenever the host picked the chassis the client's craft happened to
    /// already be (TANK, for anyone who had not been to the hangar), seat 0 was never rebuilt —
    /// and then the launch path assigned the client's own pick straight into the object seat 0
    /// was still pointing at. **Every client drew the host as its own chassis.**
    /// </summary>
    private static string? CraftBuildIsNotShared()
    {
        // The direct statement: a craft's build does not move when the loadout it was made
        // from does.
        var mine = new Loadout { Class = PlayerClass.Tank };
        var craft = new PlayerTank(Vector2.Zero, 0f, mine);
        mine.Class = PlayerClass.Fish;
        if (craft.Build.Class != PlayerClass.Tank)
            return $"editing a loadout turned an existing craft into a {craft.Build.Class}";
        if (craft.Class != craft.Build.Class)
            return "a craft's Class and the build the renderer draws it from disagree";

        // And the same thing through the whole session, which is where it actually bit: the
        // host takes the chassis the client's own craft already was, so seat 0 is never
        // rebuilt from a snapshot — and the client then picks something else.
        Session3Plus s = OpenRoom(1, new MatchSettings { MaxPlayers = 4, Revives = 2 });
        s.Host.Room!.PickForTest(PlayerClass.Tank);      // the same class every craft starts as
        s.All[1].Room!.PickForTest(PlayerClass.Fish);
        s.Step(40);
        s.Launch();
        s.Step(240);

        World.World cw = s.All[1].World;
        if (!s.All[1].InMatch) return "the client never came in";
        // Asserted on Build, because that is what DrawCraft reads.
        if (cw.Players[0].Build.Class != PlayerClass.Tank)
            return $"the client draws the host as a {cw.Players[0].Build.Class}, " +
                   $"not the TANK the host chose";
        if (cw.Players[1].Build.Class != PlayerClass.Fish)
            return $"the client draws itself as a {cw.Players[1].Build.Class}, not its FISH";
        // ...and the host's view of the client, for the mirror image of the same bug.
        World.World hw = s.Host.World;
        if (hw.Players[1].Build.Class != PlayerClass.Fish)
            return $"the host draws the client as a {hw.Players[1].Build.Class}, not their FISH";
        if (hw.Players[0].Build.Class != PlayerClass.Tank)
            return $"the host draws itself as a {hw.Players[0].Build.Class}";
        return null;
    }

    /// <summary>
    /// The join handshake, against the one condition it is guaranteed to meet: a P2P
    /// connection that has a handle but not yet a route. The hello sent into that window is
    /// gone for good — no reliable channel retransmits a message that never entered one — so
    /// a single-shot hello left the joiner on CONNECTING for ever with no way back but Escape.
    /// </summary>
    private static string? HelloSurvivesADeadSocket()
    {
        Session3Plus s = OpenRoom(1, new MatchSettings { MaxPlayers = 4 }, joinNow: 0);

        // The socket is not up. Everything this joiner says goes into the void.
        s.Wire.Mute(1, true);
        s.Join(1);
        s.Step(90);
        if (s.All[1].Net.LocalSeat >= 0) return "the joiner was seated through a dead socket";

        // Steam finishes punching through. Nothing re-sends the lost hello but the client.
        s.Wire.Mute(1, false);
        s.Step(180);
        if (s.All[1].Net.LocalSeat != 1)
            return $"the link came up and the joiner never asked again (seat {s.All[1].Net.LocalSeat})";
        if (s.Host.World.Occupied != 2) return "the host did not seat the joiner that got through";
        return null;
    }

    /// <summary>
    /// One player leaving is the most ordinary thing that happens in a room of five, and it
    /// used to end the match for everybody: Steam reports a closed connection to both ends, the
    /// host read its own report as "your session is dead" and tore the whole thing down. The
    /// other four were evicted because one person alt-F4'd.
    /// </summary>
    private static string? AHostOutlivesItsPlayers()
    {
        Session3Plus s = OpenRoom(3, new MatchSettings { MaxPlayers = 8, Revives = 2 });
        for (int i = 0; i < 4; i++) { s.All[i].Room!.PickForTest(PlayerClass.Tank); s.Step(15); }
        s.Launch();
        s.Step(120);

        s.Leave(2);
        s.Step(120);

        if (s.Host.Net.World is null) return "the host tore its own world down when a player left";
        if (!s.Host.InMatch) return "the host was thrown out of its own match";
        for (int i = 0; i < 4; i++)
        {
            if (i == 2) continue;
            if (!s.All[i].InMatch) return $"player {i} was evicted when player 2 left";
            if (s.All[i].World.Players.Count != 4)
                return $"player {i}'s roster fell apart when player 2 left";
        }
        // The one who left is held, frozen, for a reconnection — not deleted.
        if (!s.Host.World.Players[2].Away) return "the departed craft was not marked away";
        return null;
    }

    /// <summary>
    /// Somebody who walks out of the lobby has nothing worth holding — no history, no salvage,
    /// no position they earned — so their seat goes back in the pool. Held instead, a room that
    /// people came and went from opened its match with a row of abandoned craft standing on the
    /// grid, each one still counting against the host's seat limit.
    /// </summary>
    private static string? AbandonedSeatsAreReused()
    {
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 3, Revives = 2 });
        if (s.Host.World.Occupied != 3) return "three people did not fill three seats";
        if (!s.Host.World.Full) return "a three-of-three room did not call itself full";

        s.Leave(1);
        s.Step(60);
        if (s.Host.World.Occupied != 2) return "a lobby leaver kept their seat";
        if (s.Host.World.Full) return "the room was still full after somebody left it";
        if (s.Host.World.Players[1].Alive)
            return "the abandoned craft was left standing on the grid";
        if (s.Host.Room!.Avatars.ContainsKey(1)) return "their figure was left on the lobby floor";

        // The next person through the door gets the seat that came free, not a fourth one.
        s.Join(1);
        s.Step(150);
        if (s.All[1].Net.LocalSeat != 1)
            return $"the next joiner took seat {s.All[1].Net.LocalSeat} rather than the free one";
        if (s.Host.World.Players.Count != 3)
            return $"the roster grew to {s.Host.World.Players.Count} for a three-seat match";
        if (!s.Host.World.Players[1].Alive) return "the reused seat opened dead";
        return null;
    }

    /// <summary>A match with no room in it has to say so. Ignoring the hello — which is what
    /// used to happen — leaves the joiner watching the CONNECTING banner until they give up,
    /// with nothing anywhere to tell them why.</summary>
    private static string? AFullMatchRefusesOutLoud()
    {
        // Two seats, three machines: the host and one joiner fill it, the third is turned away.
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 2 }, joinNow: 1);
        if (!s.Host.World.Full) return "a two-of-two match did not call itself full";

        s.Join(2);
        s.Step(180);
        if (s.All[2].Net.Rejected is null)
            return "a full match ignored the third joiner instead of refusing them";
        if (s.All[2].Net.LocalSeat >= 0) return "a full match seated a third player anyway";
        if (s.All[2].Room!.Trouble is null) return "the refused joiner was left with no reason";
        if (s.Host.World.Players.Count != 2)
            return $"the refused joiner still cost the host a seat ({s.Host.World.Players.Count})";
        return null;
    }

    /// <summary>
    /// Dialling into a match that is already running. The host seats them and tells them START
    /// in the same breath, so there is no lobby moment left to choose a craft in — and before
    /// this they were dropped in permanently as the placeholder tank their hello carried, with
    /// no way ever to be anything else. Now they stand at the pod until they pick, and the pick
    /// crosses mid-match like any other.
    /// </summary>
    private static string? LateJoinerPicksTheirChassis()
    {
        // Host and one player launch; the third machine has not dialled yet.
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 4, Revives = 2 }, joinNow: 1);
        s.All[0].Room!.PickForTest(PlayerClass.Tank);
        s.All[1].Room!.PickForTest(PlayerClass.Spider);
        s.Step(40);
        s.Launch();
        s.Step(180);

        // Somebody dials in with the match already running.
        s.Join(2);
        s.Step(240);

        if (s.All[2].Net.LocalSeat < 0) return "the mid-match joiner was never seated";
        if (!s.All[2].Net.MatchStarted) return "the mid-match joiner was never told the match was on";
        if (s.All[2].InMatch)
            return "the mid-match joiner was walked into the match before choosing a craft";

        s.All[2].Room!.PickForTest(PlayerClass.Fish);
        s.Step(300);

        if (!s.All[2].InMatch) return "picking a craft did not bring the joiner in";
        int seat = s.All[2].Net.LocalSeat;
        for (int who = 0; who < 3; who++)
        {
            if (!s.All[who].InMatch) continue;
            World.World w = s.All[who].World;
            if (seat >= w.Players.Count)
                return $"player {who} cannot see the late joiner's seat at all";
            if (w.Players[seat].Class != PlayerClass.Fish)
                return $"player {who} sees the late joiner as {w.Players[seat].Class}, not a FISH";
        }
        return null;
    }

    /// <summary>
    /// A player's whole bench crosses to everybody: the points they spent and the colours they
    /// chose, not just which chassis they picked.
    ///
    /// <para>A pick used to be one chassis byte, so a build died on the machine that made it —
    /// every other player saw a default-painted craft with the machine's default stats, and
    /// the HOST simulated those defaults, which meant the shield and speed a player had bought
    /// were not the shield and speed they were played with. This is the test for the whole of
    /// that, on the two ends that can disagree: the host, and a third machine that only ever
    /// learns about it second-hand.</para>
    /// </summary>
    private static string? ABuildReachesEveryone()
    {
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 4, Revives = 2 });

        // Player 1 goes to the pod and actually uses the bench: a FISH, points dragged off
        // ammo onto hull, and a repaint of its hide.
        Loadout mine = s.All[1].Room!.MyBuild;
        mine.Class = PlayerClass.Fish;
        while (mine.Adjust(Loadout.Stat.Ammo, -1)) { }
        while (mine.Adjust(Loadout.Stat.Health, +1)) { }
        mine.CycleSwatch(PlayerClass.Fish, 0, +5);
        int paint = mine.SwatchIndex(PlayerClass.Fish, 0);
        int hull = mine.Health, ammo = mine.Ammo;
        if (hull == 5 || ammo == 5) return "the test failed to make a build worth sending";

        s.All[1].Room!.ConfirmPodForTest();          // presses READY at the pod
        s.All[0].Room!.PickForTest(PlayerClass.Tank);
        s.All[2].Room!.PickForTest(PlayerClass.Spider);
        s.Step(60);

        int seat = s.All[1].Net.LocalSeat;
        if (seat < 0) return "the builder was never seated";

        // In the room, before anybody launches: the host is simulating that seat and has to
        // have been told what it is.
        Loadout? onHost = s.Host.Net.BuildOf(seat);
        if (onHost is null) return "the host never learned the build";
        if (onHost.Health != hull || onHost.Ammo != ammo)
            return $"the host has the seat at {onHost.Health} hull / {onHost.Ammo} ammo, sent {hull}/{ammo}";
        if (onHost.SwatchIndex(PlayerClass.Fish, 0) != paint)
            return "the host did not get the paint";

        s.Launch();
        s.Step(240);

        // And every machine — the host, the builder, and the third player who was never told
        // anything directly — draws and simulates that craft from the same build.
        for (int who = 0; who < 3; who++)
        {
            if (!s.All[who].InMatch) return $"player {who} never came into the match";
            World.World w = s.All[who].World;
            if (seat >= w.Players.Count) return $"player {who} cannot see the builder's seat";
            PlayerTank craft = w.Players[seat];

            // Build.Class, not Class: the renderer reads Build, and the two drifting apart is
            // exactly the bug that made everyone see the host as their own chassis.
            if (craft.Build.Class != PlayerClass.Fish)
                return $"player {who} sees the builder as {craft.Build.Class}, not a FISH";
            if (craft.Build.Health != hull)
                return $"player {who} has the builder on {craft.Build.Health} hull, not {hull}";
            if (craft.Build.SwatchIndex(PlayerClass.Fish, 0) != paint)
                return $"player {who} draws the builder in the wrong paint";
            // The points have to reach the LIVE stats, not just sit on the build — this is
            // what decides how much punishment the host lets that craft take.
            if (MathF.Abs(craft.MaxHealth - craft.Build.MaxHealth) > 0.01f)
                return $"player {who} simulates the builder with the wrong hull";
            if (craft.MaxAmmo != craft.Build.MaxAmmo)
                return $"player {who} simulates the builder with the wrong magazine";
        }
        return null;
    }

    /// <summary>
    /// Names used to reach the match only by being copied off the lobby room at LAUNCH, so
    /// everyone who arrived after that — every rejoin, every late joiner — was a nameless craft
    /// on every screen but the host's for the rest of the match.
    /// </summary>
    private static string? NamesReachEveryoneAfterLaunch()
    {
        Session3Plus s = OpenRoom(2, new MatchSettings { MaxPlayers = 4, Revives = 2 });
        for (int i = 0; i < 3; i++) { s.All[i].Room!.PickForTest(PlayerClass.Tank); s.Step(15); }
        s.Launch();
        s.Step(180);

        for (int who = 0; who < 3; who++)
            for (int seat = 0; seat < 3; seat++)
            {
                string expect = seat == 0 ? "HOST" : $"PLAYER{seat}";
                if (s.All[who].World.NameOf(seat) != expect)
                    return $"player {who} knows seat {seat} as " +
                           $"'{s.All[who].World.NameOf(seat)}', not '{expect}'";
            }

        // Somebody leaves and a stranger takes the seat, all while the match runs.
        s.Leave(2);
        s.Step(60);
        s.All[2].Name = "STRANGER";
        s.Join(2);
        s.Step(180);
        s.All[2].Room?.PickForTest(PlayerClass.Tank);
        s.Step(180);

        int took = s.All[2].Net.LocalSeat;
        if (took < 0) return "the replacement was never seated";
        for (int who = 0; who < 3; who++)
        {
            if (!s.All[who].InMatch) continue;
            if (s.All[who].World.NameOf(took) != "STRANGER")
                return $"player {who} knows the new arrival as " +
                       $"'{s.All[who].World.NameOf(took)}', not 'STRANGER'";
        }
        return null;
    }

    /// <summary>
    /// A rules change has to reach the client's <em>world</em>, not just the readout on its
    /// lobby console. The seat count is the load-bearing one: a client refuses to grow its
    /// roster past its own MaxPlayers, so a host who widened the room after somebody joined
    /// left that person unable to see anyone past the old limit — named by every snapshot and
    /// created by none.
    /// </summary>
    private static string? RulesReachTheClientsWorld()
    {
        Session3Plus s = OpenRoom(1, new MatchSettings { MaxPlayers = 2, Revives = 2 });
        if (s.All[1].World.Match.MaxPlayers != 2)
            return $"the joiner opened on {s.All[1].World.Match.MaxPlayers} seats, not the host's 2";

        // The host widens the room at the console.
        s.Host.Room!.SetRulesForTest(new MatchSettings { MaxPlayers = 6, Revives = 4 });
        s.Step(90);

        if (s.All[1].World.Match.MaxPlayers != 6)
            return $"the widened room never reached the client's world " +
                   $"({s.All[1].World.Match.MaxPlayers} seats)";
        if (s.All[1].Room!.Match.Revives != 4)
            return "the new revive count never reached the client's lobby";
        return null;
    }
}
