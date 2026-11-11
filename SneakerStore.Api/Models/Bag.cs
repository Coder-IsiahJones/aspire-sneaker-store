namespace SneakerStore.Api.Models;

public sealed record AddToBagRequest(string SneakerId, double Size);

public sealed record BagLine(string Key, Sneaker Sneaker, double Size, long Quantity);
