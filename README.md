# Cobalt Core Easy Loader

Allows you to automatically load sprites and characters when modding Cobalt Core.  

## Table of Contents
1. [Installation](#installation)
2. [Creating a Mod](#creating-a-mod)
   1. [Setting up a Character](#setting-up-a-character)
   2. [Setting up Sprites](#setting-up-sprites)
   3. [Loading Dialogue](#loading-dialogue)
   4. [Loading Sprites](#loading-sprites)
3. [Examples](#examples)
4. [Injected Values](#injected-values)


## Installation
1. [Install Nickel](https://github.com/ITR13/CobaltCoreShipLoader/blob/main/how_to_install_nickel.md)
2. Put [EasyLoader.zip]() into your mods folder.
3. Put any Easy Loader mods in the same mods folder.

## Creating a Mod
1. Create a new .Net ClassLibrary project with Framework set to net8.0 and import Nickel.ModBuildConfig through Nuget. This is the same as creating any other mod for Nickel. The project name you enter here will be what's refered to as "ProjectName" everywhere later in this document.
2. Rename the starting class to EntryPoint and make it inherit from Mod. It should look something like this:
```cs
using Microsoft.Extensions.Logging;
using Nickel;

namespace ProjectName;

public class EntryPoint : Mod 
{
    public EntryPoint(
        ILogger logger
    )
    {
        logger.LogInformation("Hello World!");
    }
}
```
3. Create a Nickel.json file in the project:
```json
{
  "UniqueName": "YourName.ProjectName",
  "Version": "1.0.0",
  "RequiredApiVersion": "0.4.0",
  "EntryPointAssembly": "ProjectName.dll",
  "EntryPointType": "ProjectName.EntryPoint",
  "ModType": "EasyLoader",
  "Dependencies": [
    {
      "UniqueName": "ITR.EasyLoader",
      "Version": "1.0.0"
    }
  ]
}
```
Most of this is the same as for regular Nickel mods, the only difference is "ModType" being set to EasyLoader, and the added Dependencies section.

4. Press the play/run button in your IDE. If you did everything correctly the run log will contain `[info][YourName.ProjectName] Hello World!`. If your log does not contain this, you did something wrong.
5. Create a "Data" and a "Sprites" folder inside the project folder that your .csproj file is in. Edit your .csproj file and add the following:
```xml
    <ItemGroup>
        <Content Include="Sprites\**">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </Content>
        <Content Include="Data\**">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </Content>
    </ItemGroup>
    
    <Target Name="PreBuild" BeforeTargets="PreBuildEvent">
      <Exec Command="if exist &quot;$(TargetDir)Cards&quot; rmdir /S /Q &quot;$(TargetDir)Cards&quot;&#xA;if exist &quot;$(TargetDir)Data&quot; rmdir /S /Q &quot;$(TargetDir)Data&quot;&#xA;" />
    </Target>
```
This will ensure that the files get copied over to your mod directory correctly.
6. Add a png file to the "Sprites" folder. If you run the program the log should now contain `[info][YourName.ProjectName] Loaded 1 sprites`. Depending on how you want to use the mod check out:
 - [Setting up a Character](#setting-up-a-character)
 - [Setting up Sprites](#setting-up-sprites)
 - [Loading Dialogue](#loading-dialogue)
 - [Only loading sprites](#loading-sprites) 


### Setting up a Character
Use this part of the guide if you want to quickly test cards and artifacts, and deal with sprites later.

First create a Decks folder inside your Data folder, and create a "charactername.toml" file in it with the following content:
```toml
Color = [1.0, 1.0, 1.0, 1.0]
TitleColor = [0.0, 0.0, 0.0, 1.0]
```

Next change your EntryPoint class to the following:

```cs
using Microsoft.Extensions.Logging;
using Nickel;

namespace ProjectName;

public class EntryPoint : Mod 
{
    public EntryPoint(
        ILogger logger,
        Func<string, List<Type>, List<ICardEntry>> registerCards,
        Func<string, List<Type>, List<IArtifactEntry>> registerArtifacts,
        Func<string, List<Type>, List<Type>, ICharacterEntry> registerCharacter
    )
    {
        // The name of your character.
        // This needs to be the same name you used for the toml file earlier, but in lowercase
        var characterName = "charactername";
        
        
        _ = registerCards(
            characterName,
            [
                // Here you should put the list of cards you have defined in code. 
                typeof(CardA),
                typeof(CardB),
            ]
        );
        _ = registerArtifacts(
            Cycles,
            [
                // Here you should put the list of artifacts you have defined in code. 
                typeof(ArtifactA),
                typeof(ArtifactB),
            ]
        );
        _ = registerCharacter(
            Cycles,
            // Here you should put all the cards your character starts with
            [typeof(CardA), typeof(CardB)],
            // Here you should put all the artifacts your character starts with
            []
        );
    }
}
```
If you run the game now, the character should appear in the character select with the cards and artifacts working.

If you only want to register a deck without registering a character, just remove the call to registerCharacter!

### Setting up Sprites
Use this part of the guide if you want to set up a character with all sprites and animations. 

This is what the asset layout of a complete EasyLoader mod looks like:
```
Data/
    Decks/
        characterA.toml
        characterB.json
    Localization.csv
Sprites/
    Characters/
        characterA/
            mini/
                00.png
                01.png (etc.)
            neutral/
            happy/  (any emotion name here will be registered)
        characterB/ (etc.)
    Decks/
        characterA/
            bordersprite.png
            defaultcardart.png
        characterB/ (etc.)
    Cards/
        cardnameA.png
        cardnameB.png (etc.)
```
#### Data/Decks
The Decks folder contains all the deck definitions, they can be either json or toml files, toml being the easiest:
```toml
Color = [1.0, 1.0, 1.0, 1.0]
TitleColor = [0.0, 0.0, 0.0, 1.0]
```
It contains just two color fields that take in an RGBA floating point format.
You can create as many `charactername.toml` files here, where each character will be registered in the game.

#### Sprites/Characters
Sprites/Characters will contain all the character animations used several places in the game. If you want to add an animation called "happy" for a character named "test" you would need to create the folder `Sprites/Characters/test/happy/` and place at least one image with only numbers as its filename in here. Each image file will be added to the animation in sequential order and registered automatically, and can afterwards be used in the `loopTag` field in a Say node.  
You must always have a "mini" and "neutral" emotion registered, otherwise the game will be unable to load your character.

#### Sprites/Decks
Sprites/Decks contains all the images used for all cards in your deck. For each deck you should create a folder with the same name as the deck definition, and contain "bordersprite.png" and "defaultcardart.png".

#### Sprites/Cards
Sprites/Cards contains all the card-specific art. The filename should be the same as the classname of the card.

#### Data/Localization.csv
This is a comma separated csv file containing all the localization in the game. The first row should contain the values `key` and `en` in different columns. The key column is used to find the specific key, and the `en` column contains the english text. You can add any additional columns for other languages that you want, the possible languges are:
`en`, `de`, `es`, `fr`, `ja`, `ko`, `pt-br`, `ru`, `zh-hans`, `zh-hant`

If a localization is missing from the mod, the log will contain `[warn]`, followed by a list of localization keys. You can copy these into the key column of the csv file and fill them in as needed.

### Loading Dialogue
Currently EasyLoader has no easy way of loading dialogue, so for now you have to do the same steps as a regular Nickel mod to register dialogue. The only difference is that you should add `Dictionary<string, IDeckEntry> decks` to your constructor, and use the deck value from here whenever you need a key:
```cs
    public EntryPoint(
        ILogger logger,
        Dictionary<string, IDeckEntry> decks
    )
    {
        var deck = decks["charactername"].Deck;
        var localizationKey = deck.Key();
        logger.LogInformation("My character key is '{}'", localizationKey);
    }
```

### Loading Sprites
Use this part of the guide if you only want to use EasyLoader to load sprites and do everything else manually.

Change your EntryPoint class' constructor to
```cs
    public EntryPoint(
        ILogger logger,
        Dictionary<string, ISpriteEntry> sprites
    )
    {
        logger.LogInformation("Hello World!");
    }
```
All sprites in the "Sprite" directory will be in this dictionary. If you have a sprite at `Sprite/SomeFolder/SomeSprite.png`, then the dictionary will have the entry at `somefolder/somesprite` in all lowercase.  
You can use these sprites any way you would use normally loaded sprite, and follow a regular Nickel mod guide for the rest.

## Examples
If somebody makes a mod using the EasyLoader and uploads the source I'll put it here as a reference. So far there's only my own mod though.
- [Cycles](https://github.com/ITR13/CobaltCoreCycles)

## Injected Values
Here's the full list of injected values the mod adds. These can be used in your mod's constructor alongside any of the ones that come with Nickel:
- `Dictionary<string, ISpriteEntry>` sprites
  - This contains all the sprites found in the Sprites folder.
  - The entry name will be the same as the relative path to the Sprite folder, excluding the extension.
- `Dictionary<string, IDeckEntry>` decks
  - This contains all the deck definitions in the Data/Decks folder. 
  - The entry name is the same as the toml/json file name.
- `Dictionary<Deck,Dictionary<string,ICharacterAnimationEntry>>` animations
  - This contains all the animations in the Sprites/Characters folder.
  - The outer dict takes the Deck of the character as a key, you can get it with the deck dict.
- `Func<string, List<Type>, List<ICardEntry>>` RegisterCards
  - Call this to register a list of cards to a deck.
  - The first argument is the filename of the deck definition.
  - The second argument is the list of cards to register.
  - It returns the registered list of cards.
- `Func<string, List<Type>, List<IArtifactEntry>>` RegisterArtifacts
  - Call this to register a list of artifacts to a deck.
  - The first argument is the filename of the deck definition.
  - The second argument is the list of artifacts to register.
  - It returns the registered list of artifacts.
- `Func<string, List<Type>, List<Type>, ICharacterEntry>` RegisterCharacter
  - Call this to register a character to the character select
  - The first argument is the filename of the deck definition.
  - The second argument is the list of cards the character starts with.
  - The third argument is the list of artifacts the character starts with.
  - It returns the registered character.
- `Func<string, List<Type>, List<Type>, bool, ICharacterEntry>` RegisterCharacter
  - This is the same as the previous method, but contains a fourth argument startLocked.
  - When startLocked is true, the character will not be selectable in the character select screen.  
- `Func<string, SingleLocalizationProvider?>` RequestLocalization
  - Call this to create a SingleLocalizationProvider from a row in Localization.csv.
  - The first argument is a string that corresponds to a value in the key column.
  - It returns a SingleLocalizationProvider which can be passed into Nickel.
  - Any calls to this that fails to find a entry will get logged in the console