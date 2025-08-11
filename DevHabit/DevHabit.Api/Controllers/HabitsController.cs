﻿using System.Dynamic;
using System.Linq.Dynamic.Core;
using System.Net.Mime;
using Asp.Versioning;
using DevHabit.Api.Database;
using DevHabit.Api.DTOs.Common;
using DevHabit.Api.DTOs.Habits;
using DevHabit.Api.Entities;
using DevHabit.Api.Services;
using DevHabit.Api.Services.Sorting;
using FluentValidation;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevHabit.Api.Controllers;

[ApiController]
[Route("habits")]
[ApiVersion(1.0)]
[ApiVersion(2.0)]
public sealed class HabitsController(
    ApplicationDbContext dbContext,
    LinkService linkService) : ControllerBase
{
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json, CustomMediaTypeNames.Application.HateoasJson)]
    public async Task<IActionResult> GetHabits(
        [FromQuery] HabitsQueryParameter query,
        SortMappingProvider sortMappingProvider,
        DataShapingService dataShapingService)
    {
        if (!sortMappingProvider.ValidateMappings<HabitDto, Habit>(query.Sort))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided sort parameter isn't valid: '{query.Sort}'");
        }

        if (!dataShapingService.Validate<HabitDto>(query.Fields))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shapig fields aren't valid: '{query.Sort}'");
        }

        query.Search ??= query.Search?.Trim().ToLower();

        SortMapping[] sortMapings = sortMappingProvider.GetMappings<HabitDto, Habit>();

        IQueryable<HabitDto> habitsQuery = dbContext
                    .Habits
                    .Where(h => query.Search == null ||
                                h.Name.Contains(query.Search, StringComparison.CurrentCultureIgnoreCase) ||
                                h.Description != null && h.Description.Contains(query.Search, StringComparison.CurrentCultureIgnoreCase))
                    .Where(h => query.Type == null || h.Type == query.Type)
                    .Where(h => query.Status == null || h.Status == query.Status)
                    .ApplySort(query.Sort, sortMapings)
                    .Select(HabitQueries.ProjectToDto());

        int totalCount = await habitsQuery.CountAsync();

        List<HabitDto> habits = await habitsQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        bool includeLinks = query.Accept == CustomMediaTypeNames.Application.HateoasJson;

        var paginationResult = new PaginationResult<ExpandoObject>
        {
            Items = dataShapingService.ShapeCollectionData(
                habits,
                query.Fields,
                includeLinks ? h => CreateLinksForHabit(h.Id, query.Fields) : null),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
        };

        if (includeLinks)
        {
            paginationResult.Links = CreateLinksForHabits(
                       query,
                       paginationResult.HasNextPage,
                       paginationResult.HasPreviousPage);
        }

        return Ok(paginationResult);
    }

    [HttpGet("{id}")]
    [MapToApiVersion(1.0)]
    public async Task<IActionResult> GetHabit(
        string id,
        string? fields,
        [FromHeader(Name ="Accept")]
        string? accept,
        DataShapingService dataShapingService)
    {
        if (!dataShapingService.Validate<HabitWithTagsDto>(fields))
        {
            return Problem(
            statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shapig fields aren't valid: '{fields}'");
        }

        HabitWithTagsDto? habit = await dbContext
            .Habits
            .Where(h => h.Id == id)
            .Select(HabitQueries.ProjectToDtoWithTags())
            .FirstOrDefaultAsync();

        if (habit is null)
        {
            return NotFound();
        }

        ExpandoObject shapedHabitDto = dataShapingService.ShapeData(habit, fields);

        if (accept == CustomMediaTypeNames.Application.HateoasJson)
        {
            List<LinkDto> links = CreateLinksForHabit(id, fields);

            shapedHabitDto.TryAdd("links", links);
        }

        return Ok(shapedHabitDto);
    }

    [HttpGet("{id}")]
    [MapToApiVersion(2.0)]
    public async Task<IActionResult> GetHabitV2(
    string id,
    string? fields,
    [FromHeader(Name ="Accept")]
        string? accept,
    DataShapingService dataShapingService)
    {
        if (!dataShapingService.Validate<HabitWithTagsDtoV2>(fields))
        {
            return Problem(
            statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shapig fields aren't valid: '{fields}'");
        }

        HabitWithTagsDtoV2? habit = await dbContext
            .Habits
            .Where(h => h.Id == id)
            .Select(HabitQueries.ProjectToDtoWithTagsV2())
            .FirstOrDefaultAsync();

        if (habit is null)
        {
            return NotFound();
        }

        ExpandoObject shapedHabitDto = dataShapingService.ShapeData(habit, fields);

        if (accept == CustomMediaTypeNames.Application.HateoasJson)
        {
            List<LinkDto> links = CreateLinksForHabit(id, fields);

            shapedHabitDto.TryAdd("links", links);
        }

        return Ok(shapedHabitDto);
    }

    [HttpPost]
    public async Task<ActionResult<HabitDto>> CreateHabit(
        CreateHabitDto createHabitDto,
        IValidator<CreateHabitDto> validator)
    {
        await validator.ValidateAndThrowAsync(createHabitDto);

        Habit habit = createHabitDto.ToEntity();

        dbContext.Habits.Add(habit);

        await dbContext.SaveChangesAsync();

        HabitDto habitDto = habit.ToDto();

        habitDto.Links = CreateLinksForHabit(habit.Id, null);

        return CreatedAtAction(nameof(GetHabit), new { id = habitDto.Id }, habitDto);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateHabit(string id, UpdateHabitDto updateHabitDto)
    {
        Habit? habit = await dbContext.Habits.FirstOrDefaultAsync(h => h.Id == id);

        if (habit is null)
        {
            return NotFound();
        }

        habit.UpdateFromDto(updateHabitDto);

        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult> PatchHabit(string id, JsonPatchDocument<HabitDto> patchDocument)
    {
        Habit? habit = await dbContext.Habits.FirstOrDefaultAsync(h => h.Id == id);

        if (habit is null)
        {
            return NotFound();
        }

        HabitDto habitDto = habit.ToDto();

        patchDocument.ApplyTo(habitDto, ModelState);

        if (!TryValidateModel(habitDto))
        {
            return ValidationProblem(ModelState);
        }

        habit.Name = habitDto.Name;
        habit.Description = habitDto.Description;
        habit.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeeleteHabit(string id)
    {
        Habit? habit = await dbContext.Habits.FirstOrDefaultAsync(h => h.Id == id);

        if (habit is null)
        {
            return NotFound();
            // StatusCode(StatusCodes.Status410Gone); // Soft-delete, already deleted
        }

        dbContext.Habits.Remove(habit);

        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    private List<LinkDto> CreateLinksForHabits(HabitsQueryParameter parameters,
        bool hasNextPage,
        bool hasPreviousPage)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetHabits), "self",HttpMethods.Get, new
            {
                page = parameters.Page,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                q = parameters.Search,
                sort = parameters.Sort,
               type = parameters.Type,
               status = parameters.Status
            }),
            linkService.Create(nameof(CreateHabit),"create",HttpMethods.Post)
        ];

        if (hasNextPage)
        {
            links.Add(linkService.Create(nameof(GetHabits), "next-page", HttpMethods.Get, new
            {
                page = parameters.Page + 1,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                q = parameters.Search,
                sort = parameters.Sort,
                type = parameters.Type,
                status = parameters.Status
            }));
        }

        if (hasPreviousPage)
        {
            links.Add(linkService.Create(nameof(GetHabits), "previous-page", HttpMethods.Get, new
            {
                page = parameters.Page - 1,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                q = parameters.Search,
                sort = parameters.Sort,
                type = parameters.Type,
                status = parameters.Status
            }));
        }

        return links;
    }
    private List<LinkDto> CreateLinksForHabit(string id, string? fields)
    {
        List<LinkDto> links =
            [
                linkService.Create(nameof(GetHabit),"self",HttpMethods.Get,new {id , fields}),
                linkService.Create(nameof(UpdateHabit),"update",HttpMethods.Put,new {id }),
                linkService.Create(nameof(PatchHabit),"partial-update",HttpMethods.Patch,new {id }),
                linkService.Create(nameof(DeeleteHabit),"delete",HttpMethods.Delete,new {id }),
                linkService.Create(
                    nameof(HabitTagsController.UpsertHabitTags),
                    "upsert-tags",
                    HttpMethods.Put,
                    new {habitId= id },
                    HabitTagsController.Name)
            ];

        return links;
    }
}
