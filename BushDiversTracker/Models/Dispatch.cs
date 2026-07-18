using BushDiversTracker.Models.Enums;
using System.Text.Json.Serialization;

namespace BushDiversTracker.Models
{
    class Dispatch
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("departure_airport_id")]
        public string Departure { get; set; }
        [JsonPropertyName("destination_airport_id")]
        public string Arrival { get; set; }
        [JsonPropertyName("departure_airport_lat")]
        public decimal DepLat { get; set; }
        [JsonPropertyName("departure_airport_lon")]
        public decimal DepLon { get; set; }
        [JsonPropertyName("destination_airport_lat")]
        public decimal ArrLat { get; set; }
        [JsonPropertyName("destination_airport_lon")]
        public decimal ArrLon { get; set; }
        [JsonPropertyName("name")]
        public string Aircraft { get; set; }
        [JsonPropertyName("registration")]
        public string Registration { get; set; }
        [JsonPropertyName("aircraft_type")]
        public string AircraftType { get; set; }
        [JsonPropertyName("planned_fuel")]
        public decimal PlannedFuel { get; set; }
        [JsonPropertyName("fuel_type")]
        public FuelType? FuelType { get; set; }
        [JsonPropertyName("cargo_weight")]
        public decimal CargoWeight { get; set; }
        [JsonPropertyName("passenger_count")]
        public int PassengerCount { get; set; }
        [JsonPropertyName("total_payload")]
        public decimal TotalPayload { get; set; }
        [JsonPropertyName("payload_capacity")]
        public decimal PayloadCapacity { get; set; }
        [JsonPropertyName("is_empty")]
        public int IsEmpty { get; set; }
        [JsonPropertyName("tour")]
        public string? Tour { get; set; }
        [JsonPropertyName("cargo")]
        public DispatchCargo[] Cargo { get; set; }
    }
}
