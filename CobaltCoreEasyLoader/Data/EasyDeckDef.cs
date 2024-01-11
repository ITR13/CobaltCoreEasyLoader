namespace CobaltCoreEasyLoader.Data;

[Serializable]
public struct EasyDeckDef
{
    public double[] Color = [1.0f, 1.0f, 1.0f, 1.0f];
    public double[] TitleColor = [0.0f, 0.0f, 0.0f, 1.0f];
    
    public EasyDeckDef()
    {
    }

    // Implicit conversion from EasyDeckDef to DeckDef
    public static implicit operator DeckDef(EasyDeckDef easyDeckDef)
    {
        return new()
        {
            color = new Color(GetColor(0), GetColor(1), GetColor(2), GetColor(3)),
            titleColor = new Color(GetTitleColor(0), GetTitleColor(1), GetTitleColor(2), GetTitleColor(3)),
        };

        double GetColor(int i)
        {
            return easyDeckDef.Color.Length > i ? easyDeckDef.Color[i] : 1.0f;
        }

        double GetTitleColor(int i)
        {
            return easyDeckDef.TitleColor.Length > i ? easyDeckDef.TitleColor[i] : 1.0f;
        }
    }
}