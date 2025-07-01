public static class Helper
{
    public static float RangeTo01(float value, float min, float max) => (value - min) / (max - min);
    
    //https://stackoverflow.com/questions/929103/convert-a-number-range-to-another-range-maintaining-ratio
    /// <summary>
    /// Remaps a value from one range to another
    /// </summary>
    /// <param name="value">The value to be remapped</param>
    /// <param name="from1">Range start of the old value</param>
    /// <param name="to1">Range end of the old value</param>
    /// <param name="from2">New range start</param>
    /// <param name="to2">New range end</param>
    public static float RemapRange(float value, float from1, float to1, float from2, float to2) => (value - from1) / (to1 - from1) * (to2 - from2) + from2;
}