namespace MorpheHouse.Tycoon
{
    /// <summary>하루 영업 결과.</summary>
    public sealed class DayResult
    {
        public float Seats;
        public float Capacity;
        public float Demand;
        public float Served;
        public float Price;
        public float Satisfaction;
        public float Gross;
        public float Ingredient;
        public float Upkeep;
        public float Net;
        public float RepDelta;
        public float FatigueAdded;
        public string EventText = string.Empty;
    }
}
