namespace CobaltCoreEasyLoader.Data;

public struct EasyCharacter
{
    public List<string> StarterCards = new();
    public List<string> StarterArtifacts = new();
    public bool StartLocked = false;

    public EasyCharacter()
    {
    }
}