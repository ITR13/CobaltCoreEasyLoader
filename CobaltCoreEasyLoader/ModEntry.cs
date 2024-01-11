﻿/*using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nanoray.PluginManager;
using Nickel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shockah.Dracula;

public sealed class ModEntry : SimpleMod
{
	internal static ModEntry Instance { get; private set; } = null!;

	internal Harmony Harmony { get; }
	internal IKokoroApi KokoroApi { get; }
	internal ILocalizationProvider<IReadOnlyList<string>> AnyLocalizations { get; }
	internal ILocaleBoundNonNullLocalizationProvider<IReadOnlyList<string>> Localizations { get; }

	internal IDeckEntry DraculaDeck { get; }

	internal IStatusEntry BleedingStatus { get; }
	internal IStatusEntry BloodMirrorStatus { get; }
	internal IStatusEntry TransfusionStatus { get; }
	internal IStatusEntry TransfusingStatus { get; }

	internal ISpriteEntry ShieldCostOff { get; }
	internal ISpriteEntry ShieldCostOn { get; }
	internal ISpriteEntry BleedingCostOff { get; }
	internal ISpriteEntry BleedingCostOn { get; }
	internal ISpriteEntry HullBelowHalf { get; }
	internal ISpriteEntry HullAboveHalf { get; }

	internal ISpriteEntry BatIcon { get; }
	internal ISpriteEntry BatSprite { get; }

	internal IShipEntry Ship { get; }
	internal IPartEntry ShipWing { get; }
	internal IPartEntry ShipArmoredWing { get; }

	internal static IReadOnlyList<Type> StarterCardTypes { get; } = [
		typeof(BiteCard),
		typeof(BloodShieldCard),
	];

	internal static IReadOnlyList<Type> CommonCardTypes { get; } = [
		typeof(ClonedLeechCard),
		typeof(DrainEssenceCard),
		typeof(BatFormCard),
		typeof(BloodMirrorCard),
		typeof(GrimoireOfSecretsCard),
		typeof(SummonBatCard),
		typeof(DeathCoilCard),
	];

	internal static IReadOnlyList<Type> UncommonCardTypes { get; } = [
		typeof(AuraOfDarknessCard),
		typeof(HeartbreakCard),
		typeof(BloodScentCard),
		typeof(DispersionCard),
	];

	internal static IReadOnlyList<Type> RareCardTypes { get; } = [
		typeof(ScreechCard),
		typeof(RedThirstCard),
		typeof(CrimsonWaveCard),
	];

	internal static IReadOnlyList<Type> SecretAttackCardTypes { get; } = [
		typeof(SecretViolentCard),
		typeof(SecretPerforatingCard),
		typeof(SecretPiercingCard),
	];

	internal static IReadOnlyList<Type> SecretNonAttackCardTypes { get; } = [
		typeof(SecretProtectiveCard),
		typeof(SecretVigorousCard),
		typeof(SecretRestorativeCard),
		typeof(SecretWingedCard),
	];

	internal static IReadOnlyList<Type> ShipCards { get; } = [
		typeof(BatDebitCard),
	];

	internal static IEnumerable<Type> AllCardTypes
		=> StarterCardTypes
			.Concat(CommonCardTypes)
			.Concat(UncommonCardTypes)
			.Concat(RareCardTypes)
			.Append(typeof(PlaceholderSecretCard))
			.Concat(SecretAttackCardTypes)
			.Concat(SecretNonAttackCardTypes)
			.Concat(ShipCards);

	internal static IReadOnlyList<Type> ShipArtifacts { get; } = [
		typeof(BatmobileArtifact),
		typeof(BloodBankArtifact),
		typeof(ABTypeArtifact),
		typeof(OTypeArtifact),
	];

	internal static IEnumerable<Type> AllArtifactTypes
		=> ShipArtifacts;

	public ModEntry(IPluginPackage<IModManifest> package, IModHelper helper, ILogger logger) : base(package, helper, logger)
	{
		Instance = this;
		Harmony = new(package.Manifest.UniqueName);
		KokoroApi = helper.ModRegistry.GetApi<IKokoroApi>("Shockah.Kokoro")!;
		KokoroApi.RegisterTypeForExtensionData(typeof(AHurt));
		KokoroApi.RegisterTypeForExtensionData(typeof(AAttack));
		_ = new BleedingManager();
		_ = new BloodMirrorManager();
		_ = new LifestealManager();
		_ = new TransfusionManager();
		_ = new NegativeOverdriveManager();
		CustomTTGlossary.ApplyPatches(Harmony);

		ASpecificCardOffering.ApplyPatches(Harmony, logger);

		this.AnyLocalizations = new JsonLocalizationProvider(
			tokenExtractor: new SimpleLocalizationTokenExtractor(),
			localeStreamFunction: locale => package.PackageRoot.GetRelativeFile($"i18n/{locale}.json").OpenRead()
		);
		this.Localizations = new MissingPlaceholderLocalizationProvider<IReadOnlyList<string>>(
			new CurrentLocaleOrEnglishLocalizationProvider<IReadOnlyList<string>>(this.AnyLocalizations)
		);

		DraculaDeck = helper.Content.Decks.RegisterDeck("Dracula", new()
		{
			Definition = new() { color = DB.decks[Deck.dracula].color, titleColor = DB.decks[Deck.dracula].titleColor },
			DefaultCardArt = StableSpr.cards_colorless,
			BorderSprite = DB.deckBorders[Deck.dracula],
			Name = this.AnyLocalizations.Bind(["character", "name"]).Localize
		});

		ShieldCostOff = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/ShieldCostOff.png"));
		ShieldCostOn = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/ShieldCostOn.png"));
		BleedingCostOff = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/BleedingCostOff.png"));
		BleedingCostOn = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/BleedingCostOn.png"));
		HullBelowHalf = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/HullBelowHalf.png"));
		HullAboveHalf = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/HullAboveHalf.png"));

		BatIcon = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Icons/Bat.png"));
		BatSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Bat.png"));

		BleedingStatus = helper.Content.Statuses.RegisterStatus("Bleeding", new()
		{
			Definition = new()
			{
				icon = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Status/Bleeding.png")).Sprite,
				color = new("BE0000")
			},
			Name = this.AnyLocalizations.Bind(["status", "Bleeding", "name"]).Localize,
			Description = this.AnyLocalizations.Bind(["status", "Bleeding", "description"]).Localize
		});
		BloodMirrorStatus = helper.Content.Statuses.RegisterStatus("BloodMirror", new()
		{
			Definition = new()
			{
				icon = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Status/BloodMirror.png")).Sprite,
				color = new("FF7F7F")
			},
			Name = this.AnyLocalizations.Bind(["status", "BloodMirror", "name"]).Localize,
			Description = this.AnyLocalizations.Bind(["status", "BloodMirror", "description"]).Localize
		});
		TransfusionStatus = helper.Content.Statuses.RegisterStatus("Transfusion", new()
		{
			Definition = new()
			{
				icon = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Status/Transfusion.png")).Sprite,
				color = new("267F00")
			},
			Name = this.AnyLocalizations.Bind(["status", "Transfusion", "name"]).Localize,
			Description = this.AnyLocalizations.Bind(["status", "Transfusion", "description"]).Localize
		});
		TransfusingStatus = helper.Content.Statuses.RegisterStatus("Transfusing", new()
		{
			Definition = new()
			{
				icon = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Status/Transfusing.png")).Sprite,
				color = new("267F00")
			},
			Name = this.AnyLocalizations.Bind(["status", "Transfusing", "name"]).Localize,
			Description = this.AnyLocalizations.Bind(["status", "Transfusing", "description"]).Localize
		});

		foreach (var cardType in AllCardTypes)
			AccessTools.DeclaredMethod(cardType, nameof(IDraculaCard.Register))?.Invoke(null, [helper]);
		foreach (var artifactType in AllArtifactTypes)
			AccessTools.DeclaredMethod(artifactType, nameof(IDraculaCard.Register))?.Invoke(null, [helper]);

		helper.Content.Characters.RegisterCharacter("Dracula", new()
		{
			Deck = DraculaDeck.Deck,
			Description = this.AnyLocalizations.Bind(["character", "description"]).Localize,
			BorderSprite = StableSpr.panels_enemy_nodeck,
			StarterCardTypes = StarterCardTypes,
			NeutralAnimation = new()
			{
				Deck = DraculaDeck.Deck,
				LoopTag = "neutral",
				Frames = [
					StableSpr.characters_dracula_dracula_neutral_0,
					StableSpr.characters_dracula_dracula_neutral_1,
					StableSpr.characters_dracula_dracula_neutral_2,
					StableSpr.characters_dracula_dracula_neutral_3,
					StableSpr.characters_dracula_dracula_neutral_4,
				]
			},
			MiniAnimation = new()
			{
				Deck = DraculaDeck.Deck,
				LoopTag = "mini",
				Frames = [
					helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Character/Mini.png")).Sprite
				]
			}
		});

		helper.Content.Characters.RegisterCharacterAnimation("GameOver", new()
		{
			Deck = DraculaDeck.Deck,
			LoopTag = "gameover",
			Frames = Enumerable.Range(0, 1)
				.Select(i => helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile($"assets/Character/GameOver/{i}.png")).Sprite)
				.ToList()
		});
		helper.Content.Characters.RegisterCharacterAnimation("Squint", new()
		{
			Deck = DraculaDeck.Deck,
			LoopTag = "squint",
			Frames = Enumerable.Range(0, 5)
				.Select(i => helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile($"assets/Character/Squint/{i}.png")).Sprite)
				.ToList()
		});

		ShipWing = helper.Content.Ships.RegisterPart("Batmobile.Wing", new()
		{
			Sprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/Wing.png")).Sprite
		});
		ShipArmoredWing = helper.Content.Ships.RegisterPart("Batmobile.Wing.Armored", new()
		{
			Sprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/WingArmored.png")).Sprite
		});
		var shipCockpit = helper.Content.Ships.RegisterPart("Batmobile.Cockpit", new()
		{
			Sprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/Cockpit.png")).Sprite
		});
		var shipCannon = helper.Content.Ships.RegisterPart("Batmobile.Cannon", new()
		{
			Sprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/Cannon.png")).Sprite
		});
		var shipBay = helper.Content.Ships.RegisterPart("Batmobile.Bay", new()
		{
			Sprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/Bay.png")).Sprite
		});

		Ship = helper.Content.Ships.RegisterShip("Batmobile", new()
		{
			Ship = new()
			{
				ship = new Ship
				{
					hull = 12,
					hullMax = 12,
					shieldMaxBase = 3,
					parts =
					{
						new Part
						{
							type = PType.wing,
							skin = ShipWing.UniqueName,
							damageModifier = PDamMod.weak
						},
						new Part
						{
							type = PType.cockpit,
							skin = shipCockpit.UniqueName
						},
						new Part
						{
							type = PType.cannon,
							skin = shipCannon.UniqueName
						},
						new Part
						{
							type = PType.missiles,
							skin = shipBay.UniqueName
						},
						new Part
						{
							type = PType.wing,
							skin = ShipWing.UniqueName,
							damageModifier = PDamMod.weak,
							flip = true
						}
					}
				},
				artifacts =
				{
					new ShieldPrep(),
					new BatmobileArtifact(),
					new BloodBankArtifact(),
				},
				cards =
				{
					new CannonColorless(),
					new CannonColorless(),
					new DodgeColorless(),
					new BasicShieldColorless(),
				}
			},
			UnderChassisSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("assets/Ship/Chassis.png")).Sprite,
			ExclusiveArtifactTypes = new HashSet<Type>()
			{
				typeof(BatmobileArtifact),
				typeof(BloodBankArtifact),
				typeof(ABTypeArtifact),
				typeof(OTypeArtifact),
			},
			Name = this.AnyLocalizations.Bind(["ship", "name"]).Localize,
			Description = this.AnyLocalizations.Bind(["ship", "description"]).Localize,
		});
	}
}*/