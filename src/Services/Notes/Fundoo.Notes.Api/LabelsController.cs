using Fundoo.Notes.Application;
using Fundoo.Notes.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notes.Api;

[ApiController]
[Authorize]
[Route("api/labels")]
public class LabelsController : ControllerBase
{
    private readonly ILabelRepository _labels;

    public LabelsController(ILabelRepository labels)
    {
        _labels = labels;
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public Task<List<Label>> Get() =>
        _labels.GetLabelsAsync(CurrentUserId());

    [HttpPost]
    public async Task<IActionResult> Create(CreateLabelRequest request)
    {

        var label = new Label
        {
            Id = Guid.NewGuid(),
            UserId = CurrentUserId(),
            Name = request.Name.Trim()
        };

        await _labels.AddLabelAsync(label);

        return Created("", label);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(
        Guid id,
        RenameLabelRequest request) =>
        await _labels.RenameLabelAsync(
            CurrentUserId(), id, request.Name.Trim())
            ? NoContent()
            : NotFound();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await _labels.DeleteLabelAsync(CurrentUserId(), id)
            ? NoContent()
            : NotFound();
}