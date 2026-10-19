using UltimatePos.Application.Business;
using UltimatePos.Application.Common;
using UltimatePos.Application.Common.Helpers;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Branches
{
    /// <summary>
    /// The branches of the business signed in to, and who may work in each. A business always has its head office (number 00), which
    /// cannot be closed; further branches take the next free number, as KRA eTIMS numbers branches.
    /// </summary>
    public class BranchService
    {
        private readonly IBranchRepository _repository;
        private readonly IBusinessRepository _businesses;
        private readonly IAuthRepository _auth;
        private readonly ITenantContext _tenant;
        private readonly IBranchContext _branches;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;

        public BranchService(IBranchRepository repository, IBusinessRepository businesses, IAuthRepository auth, ITenantContext tenant,
            IBranchContext branches, ICurrentUser currentUser, ISystemEventRecorder events)
        {
            _repository = repository;
            _businesses = businesses;
            _auth = auth;
            _tenant = tenant;
            _branches = branches;
            _currentUser = currentUser;
            _events = events;
        }

        /// <summary>The branches the caller may see: all of them with Branches.AccessAll, otherwise their own.</summary>
        public async Task<IEnumerable<BranchDto>> GetBranchesAsync(bool includeInactive = false)
        {
            _tenant.RequireBusinessId();
            return (await _repository.GetBranchesAsync(includeInactive)).Where(b => _branches.CanSee(b.BranchId)).Select(ToDto);
        }

        public async Task<BranchDto> GetBranchAsync(Guid branchId) => ToDto(await RequireVisibleAsync(branchId));

        public async Task<BranchDto> CreateBranchAsync(CreateBranchRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            await EnsureBranchAllowanceAsync(businessId);

            var shortCode = BusinessService.NormalizeShortCode(request.MpesaShortCode);
            await ValidateAsync(request.Name, request.Code, shortCode, null);

            int number;
            if (request.BranchNumber is { } requested)
            {
                if (requested is < 1 or > Branch.MaxBranchNumber)
                    throw new ValidationException("branchNumber", $"A branch number is 1 to {Branch.MaxBranchNumber}; 0 is the head office.");
                if (await _repository.BranchNumberExistsAsync(requested))
                    throw new ValidationException("branchNumber", $"Branch {Branch.FormatKraBranchId(requested)} already exists (numbers are never reused).");
                number = requested;
            }
            else
            {
                number = await _repository.NextBranchNumberAsync();
                if (number > Branch.MaxBranchNumber)
                    throw new InvalidAssignmentException($"All {Branch.MaxBranchNumber} branch numbers are in use.");
            }

            var branch = new Branch
            {
                BranchId = Guid.NewGuid(),
                BusinessId = businessId,
                BranchNumber = number,
                Name = request.Name.Trim(),
                Code = Clean(request.Code)?.ToUpperInvariant(),
                PhysicalAddress = Clean(request.PhysicalAddress),
                County = Clean(request.County),
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = Clean(request.Email),
                MpesaShortCode = shortCode,
                CreatedBy = _currentUser.UserId,
            };
            _events.Record(new SystemEventDraft(SystemEventTypes.BranchCreated, $"Branch {branch.KraBranchId} {branch.Name} was opened.",
                EntityType: "Branch", EntityId: branch.BranchId, Data: new { branch.BranchNumber, branch.Name, branch.Code }));
            return ToDto(await _repository.CreateBranchAsync(branch));
        }

        public async Task<BranchDto> UpdateBranchAsync(Guid branchId, UpdateBranchRequestDto request)
        {
            await RequireVisibleAsync(branchId);
            var shortCode = BusinessService.NormalizeShortCode(request.MpesaShortCode);
            await ValidateAsync(request.Name, request.Code, shortCode, branchId);

            var updated = await _repository.UpdateBranchAsync(branchId, b =>
            {
                b.Name = request.Name.Trim();
                b.Code = Clean(request.Code)?.ToUpperInvariant();
                b.PhysicalAddress = Clean(request.PhysicalAddress);
                b.County = Clean(request.County);
                b.PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber);
                b.Email = Clean(request.Email);
                b.MpesaShortCode = shortCode;
                b.LastUpdatedBy = _currentUser.UserId;
                b.LastUpdatedAt = DateTime.UtcNow;
            });
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.BranchUpdated, $"Branch {updated.KraBranchId} {updated.Name} was updated.",
                EntityType: "Branch", EntityId: branchId, BranchId: branchId));
            return ToDto(updated);
        }

        /// <summary>A closed branch keeps its history and its number; it can't be signed in to or chosen for new work.</summary>
        public async Task<BranchDto> SetBranchActiveAsync(Guid branchId, bool isActive)
        {
            var branch = await RequireVisibleAsync(branchId);
            if (branch.IsActive == isActive)
                throw new InvalidAssignmentException($"Branch {branch.KraBranchId} is already {(isActive ? "open" : "closed")}.");
            if (!isActive && branch.IsHeadOffice)
                throw new InvalidAssignmentException("The head office (branch 00) cannot be closed.");
            if (!isActive && await _repository.HasStockAsync(branchId))
                throw new InvalidAssignmentException($"Branch {branch.KraBranchId} still holds stock. Transfer it to another branch (or adjust it to zero) first.");
            if (isActive)
                await EnsureBranchAllowanceAsync(branch.BusinessId);

            var updated = await _repository.UpdateBranchAsync(branchId, b =>
            {
                b.IsActive = isActive;
                b.LastUpdatedBy = _currentUser.UserId;
                b.LastUpdatedAt = DateTime.UtcNow;
            });
            await _events.RecordNowAsync(new SystemEventDraft(isActive ? SystemEventTypes.BranchActivated : SystemEventTypes.BranchDeactivated,
                $"Branch {updated.KraBranchId} {updated.Name} was {(isActive ? "reopened" : "closed")}.",
                isActive ? Domain.Entities.EventSeverity.Info : Domain.Entities.EventSeverity.Warning, "Branch", branchId, BranchId: branchId));
            return ToDto(updated);
        }

        // ---- Who works where ----

        public async Task<UserBranchesDto> GetUserBranchesAsync(Guid userId)
        {
            var businessId = _tenant.RequireBusinessId();
            if (await _auth.GetBusinessUserAsync(businessId, userId) is null)
                throw new NotFoundException($"User '{userId}' not found.");

            var assigned = await _repository.GetUserBranchesAsync(userId, businessId);
            var accessAll = (await _auth.GetUserPermissionsAsync(userId, businessId)).Contains(BranchContext.AccessAllPermission);
            return new UserBranchesDto(userId, accessAll, assigned.Select(a => ToDto(a.Branch)), assigned.FirstOrDefault(a => a.IsDefault)?.BranchId);
        }

        /// <summary>Replaces the branches a user works in. Takes effect at their next sign-in or token refresh.</summary>
        public async Task<UserBranchesDto> AssignUserBranchesAsync(Guid userId, AssignUserBranchesRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            var member = await _auth.GetBusinessUserAsync(businessId, userId)
                ?? throw new NotFoundException($"User '{userId}' not found.");

            var ids = (request.BranchIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            var active = (await _repository.GetBranchesAsync(includeInactive: false)).ToDictionary(b => b.BranchId);
            var unknown = ids.Where(id => !active.ContainsKey(id)).ToList();
            if (unknown.Count > 0)
                throw new ValidationException("branchIds", "One or more branches don't exist in this business or are closed.");
            if (request.DefaultBranchId is { } d && !ids.Contains(d))
                throw new ValidationException("defaultBranchId", "The default branch must be one of the user's branches.");

            await _repository.SetUserBranchesAsync(userId, businessId, ids, request.DefaultBranchId ?? ids.FirstOrDefault(), _currentUser.UserId);
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.UserBranchesChanged,
                $"{member.User.Username} now works in: {(ids.Count == 0 ? "no branch" : string.Join(", ", ids.Select(id => $"{active[id].KraBranchId} {active[id].Name}")))}.",
                EntityType: "User", EntityId: userId, Data: new { branchIds = ids, request.DefaultBranchId }, SubjectUserId: userId));
            return await GetUserBranchesAsync(userId);
        }

        // ---- Helpers ----

        private async Task<Branch> RequireVisibleAsync(Guid branchId)
        {
            _tenant.RequireBusinessId();
            var branch = await _repository.GetBranchAsync(branchId);
            if (branch is null || !_branches.CanSee(branchId))
                throw new NotFoundException($"Branch '{branchId}' not found.");
            return branch;
        }

        /// <summary>The licence's branch limit, checked before a branch is opened or reopened.</summary>
        private async Task EnsureBranchAllowanceAsync(Guid businessId)
        {
            var business = await _businesses.GetBusinessByIdAsync(businessId);
            if (business?.LicenseMaxBranches is not { } limit)
                return;
            var active = await _repository.CountActiveBranchesAsync();
            if (active >= limit)
                throw new BusinessAccessDeniedException("BRANCH_LIMIT_REACHED",
                    $"The licence allows {limit} branch{(limit == 1 ? "" : "es")}, and {active} are open. Close one, or ask for a larger licence.");
        }

        private async Task ValidateAsync(string name, string? code, string? shortCode, Guid? branchId)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(name))
                errors["name"] = new[] { "The branch name is required." };
            if (Clean(code) is { } c)
            {
                if (c.Length > 10 || !c.All(char.IsLetterOrDigit))
                    errors["code"] = new[] { "A code is up to 10 letters or digits, e.g. NBO." };
                else if (await _repository.CodeExistsAsync(c.ToUpperInvariant(), branchId))
                    errors["code"] = new[] { $"Another branch already uses code {c.ToUpperInvariant()}." };
            }
            if (shortCode is not null && (await _repository.MpesaShortCodeInUseAsync(shortCode, branchId)
                                          || await _businesses.MpesaShortCodeExistsAsync(shortCode, includeBranches: false)))
                errors["mpesaShortCode"] = new[] { $"M-Pesa short code {shortCode} is already used by another business or branch." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        internal static BranchDto ToDto(Branch b) =>
            new(b.BranchId, b.BranchNumber, b.KraBranchId, b.Name, b.Code, b.PhysicalAddress, b.County, b.PhoneNumber, b.Email, b.MpesaShortCode,
                b.IsHeadOffice, b.IsActive, b.CreatedAt);
    }
}
