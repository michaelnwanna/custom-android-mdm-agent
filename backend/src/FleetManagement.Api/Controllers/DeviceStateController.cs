namespace FleetManagement.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// REST API for device state queries and reconciliation.
/// Provides endpoints to retrieve desired state and perform reconciliation
/// when devices reconnect after network outages.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Produces("application/json")]
public class DeviceStateController : ControllerBase
{
    private readonly FleetDbContext _context;
    private readonly StateComputationService _stateComputation;

    public DeviceStateController(FleetDbContext context, StateComputationService stateComputation)
    {
        _context = context;
        _stateComputation = stateComputation;
    }

    /// <summary>
    /// Get the desired state for a specific device.
    /// 
    /// Computes or retrieves cached desired state based on current
    /// policy, release, and tag assignments. The state is cached for 5 minutes.
    /// 
    /// Endpoint: GET /api/v1/devices/{enrollmentId}/desired-state
    /// </summary>
    /// <param name="enrollmentId">The device's enrollment ID.</param>
    /// <returns>The current desired state for the device.</returns>
    [HttpGet("{enrollmentId:guid}/desired-state")]
    [ProducesResponseType(typeof(DesiredDeviceStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DesiredDeviceStateDto>> GetDesiredState(Guid enrollmentId)
    {
        // Verify device exists
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.EnrollmentId == enrollmentId);
        
        if (device == null)
        {
            return NotFound(new { error = $"Device with enrollment ID {enrollmentId} not found." });
        }

        try
        {
            // Compute or retrieve cached desired state
            var desiredState = await _stateComputation.ComputeDesiredStateAsync(
                enrollmentId,
                forceRecompute: false
            );

            return Ok(DesiredDeviceStateDto.FromDomain(desiredState));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to compute desired state.", detail = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get the desired state without triggering recomputation.
    /// 
    /// Returns the cached desired state if available, even if expired.
    /// Does not trigger computation of new state.
    /// 
    /// Endpoint: GET /api/v1/devices/{enrollmentId}/desired-state/cached
    /// </summary>
    /// <param name="enrollmentId">The device's enrollment ID.</param>
    /// <returns>The cached desired state, or null if not cached.</returns>
    [HttpGet("{enrollmentId:guid}/desired-state/cached")]
    [ProducesResponseType(typeof(DesiredDeviceStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DesiredDeviceStateDto?>> GetCachedDesiredState(Guid enrollmentId)
    {
        try
        {
            var cachedState = await _stateComputation.GetCachedStateAsync(enrollmentId);

            if (cachedState == null)
            {
                return NotFound(new { error = $"No cached desired state for device {enrollmentId}." });
            }

            return Ok(DesiredDeviceStateDto.FromDomain(cachedState));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to retrieve cached state.", detail = ex.Message }
            );
        }
    }

    /// <summary>
    /// Perform reconciliation between device's observed and server's desired state.
    /// 
    /// Compares the device's reported state against the authoritative desired state.
    /// Detects conflicts and identifies missing commands/assignments.
    /// 
    /// This endpoint is typically called when a device reconnects after being offline.
    /// 
    /// Endpoint: POST /api/v1/devices/{enrollmentId}/reconcile
    /// </summary>
    /// <param name="enrollmentId">The device's enrollment ID.</param>
    /// <param name="request">The observed state reported by the device.</param>
    /// <returns>Reconciliation result with conflict detection and sync status.</returns>
    [HttpPost("{enrollmentId:guid}/reconcile")]
    [ProducesResponseType(typeof(ReconciliationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ReconciliationResultDto>> ReconcileDeviceState(
        Guid enrollmentId,
        [FromBody] ReconciliationRequestDto request)
    {
        if (request?.ObservedState == null)
        {
            return BadRequest(new { error = "ObservedState is required in request body." });
        }

        // Verify device exists
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.EnrollmentId == enrollmentId);
        
        if (device == null)
        {
            return NotFound(new { error = $"Device with enrollment ID {enrollmentId} not found." });
        }

        try
        {
            // Convert DTO to domain model
            var observedState = request.ObservedState.ToDomain();

            // Perform reconciliation (compares observed state against computed desired state)
            var reconciliation = await _stateComputation.ReconcileStateAsync(
                enrollmentId,
                observedState
            );

            return Ok(ReconciliationResultDto.FromDomain(reconciliation));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Reconciliation failed.", detail = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get all devices that are currently out of sync.
    /// 
    /// Returns a list of devices whose observed state differs from desired state
    /// or whose desired state has expired and needs recomputation.
    /// 
    /// Endpoint: GET /api/v1/devices/out-of-sync
    /// </summary>
    /// <returns>List of out-of-sync device IDs.</returns>
    [HttpGet("out-of-sync")]
    [ProducesResponseType(typeof(List<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<Guid>>> GetOutOfSyncDevices()
    {
        try
        {
            var outOfSyncDevices = await _stateComputation.GetOutOfSyncDevicesAsync(TimeSpan.FromMinutes(5));
            return Ok(outOfSyncDevices);
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to retrieve out-of-sync devices.", detail = ex.Message }
            );
        }
    }

    /// <summary>
    /// Invalidate the cached desired state for a device.
    /// 
    /// Forces recomputation of desired state on next query.
    /// Called when policy, release, or tag assignments change.
    /// 
    /// Endpoint: POST /api/v1/devices/{enrollmentId}/desired-state/invalidate
    /// </summary>
    /// <param name="enrollmentId">The device's enrollment ID.</param>
    /// <returns>Success response.</returns>
    [HttpPost("{enrollmentId:guid}/desired-state/invalidate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> InvalidateDeviceState(Guid enrollmentId)
    {
        // Verify device exists
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.EnrollmentId == enrollmentId);
        
        if (device == null)
        {
            return NotFound(new { error = $"Device with enrollment ID {enrollmentId} not found." });
        }

        try
        {
            await _stateComputation.InvalidateCacheAsync(enrollmentId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to invalidate device state cache.", detail = ex.Message }
            );
        }
    }
}
