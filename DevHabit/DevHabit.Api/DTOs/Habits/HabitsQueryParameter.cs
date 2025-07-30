using DevHabit.Api.Entities;
using Microsoft.AspNetCore.Mvc;

namespace DevHabit.Api.DTOs.Habits;

public sealed record HabitsQueryParameter
{
    [FromQuery(Name = "q")]
    public string? Search { get; set; }
    public HabitStatus? Status { get; set; }
    public HabitType? Type { get; set; }
    public string? Sort { get; init; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
