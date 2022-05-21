namespace PartScaler
{
    public interface IRescalable
    {
        void OnRescale(ScalingFactor factor);
    }
    public interface IRescalable<T> : IRescalable
    {
    }
}