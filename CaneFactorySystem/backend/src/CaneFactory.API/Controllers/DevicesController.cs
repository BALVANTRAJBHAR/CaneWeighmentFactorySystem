using System.Text.Json;
using CaneFactory.API.Auth;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Developer-only weighing device + string profile configuration, communication test,
/// parser test, activation/versioning and the development simulator.
/// </summary>
[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly WeighingService _weighing;

    public DevicesController(AppDbContext db, IAuditService audit, ICurrentUser current, WeighingService weighing)
    {
        _db = db; _audit = audit; _current = current; _weighing = weighing;
    }

    // ---------------------------------------------------------------- DEVICES
    [HasPermission("Device.View")]
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await _db.WeighingDevices.Where(d => !d.IsDeleted)
            .Select(d => new
            {
                d.Id, d.DeviceName, d.Manufacturer, d.ModelNumber, d.ConnectionType, d.ComPort, d.BaudRate,
                d.Parity, d.DataBits, d.StopBits, d.FlowControl, d.CharacterEncoding, d.ReadTimeoutMs,
                d.ReadIntervalMs, d.AutoReconnect, d.ReconnectAttempts, d.IsEnabled, d.ActiveConfiguration,
                d.DesiredConnectionState, d.DesiredReaderRunning,
                d.ActiveStringProfileId, ActiveStringProfileName = d.ActiveStringProfile != null ? d.ActiveStringProfile.StringProfileName : null
            }).ToListAsync());

    [HasPermission("Device.Configure")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WeighingDevice device)
    {
        device.Id = 0;
        device.CreatedBy = _current.UserId;
        _db.WeighingDevices.Add(device);
        await _db.SaveChangesAsync();
        await LogHistory(device.Id, "Create", null, device, "Saved");
        return Ok(new { message = $"Weighing device '{device.DeviceName}' saved successfully.", id = device.Id });
    }

    [HasPermission("Device.Configure")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] WeighingDevice src)
    {
        var d = await _db.WeighingDevices.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound(new { message = "Device not found." });
        var old = JsonSerializer.Serialize(d, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
        d.DeviceName = src.DeviceName; d.Manufacturer = src.Manufacturer; d.ModelNumber = src.ModelNumber;
        d.ConnectionType = src.ConnectionType; d.ComPort = src.ComPort; d.BaudRate = src.BaudRate;
        d.Parity = src.Parity; d.DataBits = src.DataBits; d.StopBits = src.StopBits; d.FlowControl = src.FlowControl;
        d.CharacterEncoding = src.CharacterEncoding; d.ReadTimeoutMs = src.ReadTimeoutMs; d.ReadIntervalMs = src.ReadIntervalMs;
        d.AutoReconnect = src.AutoReconnect; d.ReconnectAttempts = src.ReconnectAttempts; d.IsEnabled = src.IsEnabled;
        d.ActiveStringProfileId = src.ActiveStringProfileId;
        d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await LogHistory(id, "Edit", old, src, "Saved");
        return Ok(new { message = $"Device '{d.DeviceName}' configuration updated successfully." });
    }

    /// <summary>Only ONE device configuration can be ACTIVE at a time.</summary>
    [HasPermission("Device.Configure")]
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var d = await _db.WeighingDevices.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound(new { message = "Device not found." });
        if (d.ActiveStringProfileId == null) return Conflict(new { message = "Assign an active String Profile before activating." });
        var others = await _db.WeighingDevices.Where(x => x.Id != id && x.ActiveConfiguration).ToListAsync();
        foreach (var o in others) o.ActiveConfiguration = false;
        d.ActiveConfiguration = true;
        d.IsEnabled = true;
        await _db.SaveChangesAsync();
        await LogHistory(id, "Activate", null, new { d.DeviceName }, "Activated");
        return Ok(new { message = $"Configuration for '{d.DeviceName}' is now ACTIVE." });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var d = await _db.WeighingDevices.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound(new { message = "Device not found." });
        if (_weighing.IsOperatingDevice(id)) _weighing.Disconnect();
        d.ActiveConfiguration = false;
        d.DesiredConnectionState = "Disconnected";
        d.DesiredReaderRunning = false;
        await _db.SaveChangesAsync();
        await LogHistory(id, "Deactivate", null, new { d.DeviceName }, "Deactivated");
        return Ok(new { message = $"Configuration for '{d.DeviceName}' deactivated." });
    }

    [HasPermission("Device.View")]
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id) =>
        Ok(await _db.DeviceConfigHistories.Where(h => h.DeviceId == id)
            .OrderByDescending(h => h.ChangedAt).Take(100).ToListAsync());

    // ---------------------------------------------------------------- COMMUNICATION / TEST
    [HasPermission("Device.Configure")]
    [HttpGet("ports")]
    public IActionResult Ports() => Ok(new { ports = WeighingService.AvailablePorts() });

    [HasPermission("Device.Configure")]
    [HttpPost("{id:int}/connect")]
    public async Task<IActionResult> Connect(int id)
    {
        var (ok, message) = await _weighing.ConnectAsync(id);
        if (ok)
        {
            var device = await _db.WeighingDevices.FindAsync(id);
            if (device != null) { device.DesiredConnectionState = "Connected"; await _db.SaveChangesAsync(); }
        }
        await LogHistory(id, "TestConnection", null, null, ok ? "Connected" : message);
        return ok ? Ok(new { message }) : Conflict(new { message });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        _weighing.Disconnect();
        var active = await _db.WeighingDevices.Where(d => d.ActiveConfiguration).ToListAsync();
        foreach (var d in active) { d.DesiredConnectionState = "Disconnected"; d.DesiredReaderRunning = false; }
        await _db.SaveChangesAsync();
        return Ok(new { message = "Device disconnected." });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("start-reading")]
    public async Task<IActionResult> StartReading()
    {
        var (ok, message) = _weighing.StartReading();
        if (ok)
        {
            var active = await _db.WeighingDevices.FirstOrDefaultAsync(d => d.ActiveConfiguration);
            if (active != null) { active.DesiredConnectionState = "Connected"; active.DesiredReaderRunning = true; await _db.SaveChangesAsync(); }
        }
        return ok ? Ok(new { message }) : Conflict(new { message });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("stop-reading")]
    public async Task<IActionResult> StopReading()
    {
        _weighing.StopReading();
        var active = await _db.WeighingDevices.Where(d => d.ActiveConfiguration).ToListAsync();
        foreach (var d in active) d.DesiredReaderRunning = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Reading stopped." });
    }

    // Technical users who can view device configuration must be able to read its current
    // operational state too; saves remain protected by Device.Configure.
    [HasPermission("Device.View")]
    [HttpGet("live-weight")]
    public IActionResult LiveWeight() => Ok(_weighing.Current);

    /// <summary>Parser test: paste RAW HEX (e.g. "02 20 30 30 31 35 30 30 03 0D 0A") or raw text and verify parsing before activation.</summary>
    [HasPermission("Device.Configure")]
    [HttpPost("test-parser")]
    public async Task<IActionResult> TestParser(ParserTestRequest req)
    {
        StringProfile? profile = null;
        if (req.StringProfileId.HasValue)
            profile = await _db.StringProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == req.StringProfileId && !p.IsDeleted);
        if (profile == null) return NotFound(new { message = "String Profile not found." });

        byte[] frame;
        if (!string.IsNullOrWhiteSpace(req.RawHex))
        {
            try
            {
                frame = req.RawHex.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => Convert.ToByte(h, 16)).ToArray();
            }
            catch { return BadRequest(new { message = "Invalid HEX input. Example: 02 20 30 30 31 35 30 30 03 0D 0A" }); }
        }
        else if (!string.IsNullOrWhiteSpace(req.RawText))
            frame = System.Text.Encoding.ASCII.GetBytes(req.RawText);
        else
            return BadRequest(new { message = "Provide RawHex or RawText to test." });

        var parsed = ParserFactory.Create(profile).Parse(frame);
        return Ok(parsed);
    }

    // ---------------------------------------------------------------- SIMULATOR (dev/demo)
    [HasPermission("Device.Configure")]
    [HttpPost("simulator/start")]
    public async Task<IActionResult> SimulatorStart([FromBody] Dictionary<string, decimal> body)
    {
        var profile = await _db.StringProfiles.AsNoTracking().FirstOrDefaultAsync(p => !p.IsDeleted && p.Status);
        if (profile == null) return NotFound(new { message = "No string profile available." });
        var (ok, message) = _weighing.StartSimulator(profile, body.GetValueOrDefault("targetKg", 25000));
        return ok ? Ok(new { message }) : Conflict(new { message });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("simulator/set-weight")]
    public IActionResult SimulatorSet([FromBody] Dictionary<string, decimal> body)
    {
        _weighing.SetSimulatorWeight(body.GetValueOrDefault("kg", 0));
        return Ok(new { message = "Simulator weight updated.", current = _weighing.Current });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("simulator/stop")]
    public IActionResult SimulatorStop()
    {
        _weighing.StopSimulator();
        return Ok(new { message = "Simulator stopped." });
    }

    private async Task LogHistory(int deviceId, string action, object? oldCfg, object? newCfg, string result)
    {
        _db.DeviceConfigHistories.Add(new DeviceConfigHistory
        {
            DeviceId = deviceId,
            Action = action,
            OldConfigJson = oldCfg as string ?? (oldCfg == null ? null : JsonSerializer.Serialize(oldCfg)),
            NewConfigJson = newCfg == null ? null : JsonSerializer.Serialize(newCfg,
                new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles }),
            ChangedBy = _current.UserId,
            ChangedByName = _current.Username,
            Result = result
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("WeighingDeviceConfiguration", "Device", "WeighingDevice", deviceId.ToString(),
            newValue: new { Action = action, Result = result });
    }
}

[ApiController]
[Route("api/string-profiles")]
public class StringProfilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    public StringProfilesController(AppDbContext db, IAuditService audit, ICurrentUser current)
    {
        _db = db; _audit = audit; _current = current;
    }

    [HasPermission("Device.View")]
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await _db.StringProfiles.Where(p => !p.IsDeleted).OrderBy(p => p.Id).ToListAsync());

    [HasPermission("Device.Configure")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StringProfile p)
    {
        p.Id = 0;
        p.CreatedBy = _current.UserId;
        if (string.IsNullOrWhiteSpace(p.StringProfileName))
            return BadRequest(new { message = "String Profile Name is required." });
        if (await _db.StringProfiles.AnyAsync(x => !x.IsDeleted && x.StringProfileName == p.StringProfileName))
            return Conflict(new { message = $"String Profile '{p.StringProfileName}' already exists." });
        _db.StringProfiles.Add(p);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Device", "StringProfile", p.Id.ToString(), newValue: p);
        return Ok(new { message = $"String Profile '{p.StringProfileName}' saved successfully.", id = p.Id });
    }

    [HasPermission("Device.Configure")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] StringProfile src)
    {
        var p = await _db.StringProfiles.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = "String Profile not found." });
        var old = JsonSerializer.Serialize(p);
        _db.Entry(p).CurrentValues.SetValues(src);
        p.Id = id;
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Device", "StringProfile", id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"String Profile '{p.StringProfileName}' updated successfully." });
    }

    [HasPermission("Device.Configure")]
    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> Duplicate(int id)
    {
        var p = await _db.StringProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = "String Profile not found." });
        p.Id = 0;
        p.StringProfileName = $"{p.StringProfileName} (Copy)";
        p.CreatedAt = DateTime.UtcNow;
        p.CreatedBy = _current.UserId;
        _db.StringProfiles.Add(p);
        await _db.SaveChangesAsync();
        return Ok(new { message = $"String Profile duplicated as '{p.StringProfileName}'.", id = p.Id });
    }

    [HasPermission("Device.Configure")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Disable(int id)
    {
        var p = await _db.StringProfiles.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = "String Profile not found." });
        if (await _db.WeighingDevices.AnyAsync(d => d.ActiveStringProfileId == id && !d.IsDeleted))
            return Conflict(new { message = "This profile is assigned to a weighing device and cannot be deleted." });
        p.IsDeleted = true;
        p.DeletedAt = DateTime.UtcNow;
        p.DeletedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Device", "StringProfile", id.ToString());
        return Ok(new { message = $"String Profile '{p.StringProfileName}' disabled." });
    }
}
