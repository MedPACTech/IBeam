using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Users;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace IBeam.Identity.Services.Auth;

public sealed class OAuthAuthService : IIdentityOAuthAuthService
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OAuthOptions> _oauthOptions;
    private readonly IIdentityUserStore _users;
    private readonly IExternalLoginStore _externalLogins;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly IOptions<TenantProvisioningOptions> _tenantProvisioningOptions;
    private readonly ITenantInfoResolver _tenantInfoResolver;
    private readonly ITenantExtensionCoordinator _tenantExtensions;
    private readonly IIdentityUserExtensionCoordinator _userExtensions;
    private readonly ILogger<OAuthAuthService> _logger;
    private readonly IServiceOperationExecutor _operations;

    public OAuthAuthService(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OAuthOptions> oauthOptions,
        IIdentityUserStore users,
        IExternalLoginStore externalLogins,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        ILogger<OAuthAuthService> logger)
        : this(
            cache,
            httpClientFactory,
            oauthOptions,
            users,
            externalLogins,
            tenants,
            tenantProvisioning,
            tokens,
            eventPublisher,
            lifecycleHook,
            eventOptions,
            tenantProvisioningOptions,
            new TenantInfoResolver(new NoOpTenantMetadataProvider()),
            new NoOpTenantExtensionCoordinator(),
            new NoOpIdentityUserExtensionCoordinator(),
            logger)
    {
    }

    public OAuthAuthService(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OAuthOptions> oauthOptions,
        IIdentityUserStore users,
        IExternalLoginStore externalLogins,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        ITenantInfoResolver tenantInfoResolver,
        ITenantExtensionCoordinator tenantExtensions,
        IIdentityUserExtensionCoordinator userExtensions,
        ILogger<OAuthAuthService> logger,
        IServiceOperationExecutor? operations = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _oauthOptions = oauthOptions ?? throw new ArgumentNullException(nameof(oauthOptions));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _externalLogins = externalLogins ?? throw new ArgumentNullException(nameof(externalLogins));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _tenantProvisioningOptions = tenantProvisioningOptions ?? throw new ArgumentNullException(nameof(tenantProvisioningOptions));
        _tenantInfoResolver = tenantInfoResolver ?? throw new ArgumentNullException(nameof(tenantInfoResolver));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
        _userExtensions = userExtensions ?? throw new ArgumentNullException(nameof(userExtensions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operations = operations ?? new ServiceOperationExecutor();
    }

    public OAuthAuthService(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OAuthOptions> oauthOptions,
        IIdentityUserStore users,
        IExternalLoginStore externalLogins,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens)
        : this(
            cache,
            httpClientFactory,
            oauthOptions,
            users,
            externalLogins,
            tenants,
            tenantProvisioning,
            tokens,
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Microsoft.Extensions.Options.Options.Create(new AuthEventOptions()),
            Microsoft.Extensions.Options.Options.Create(new TenantProvisioningOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OAuthAuthService>.Instance)
    {
    }

    [IBeamOperation("identity.auth.oauth.start", Permission = false)]
    public Task<OAuthStartResponse> StartOAuthAsync(string provider, string redirectUri, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            _ => StartOAuthInternalAsync(provider, redirectUri, linkUserId: null),
            new ServiceOperationExecutionOptions { PermissionEnabled = false },
            ct);

    [IBeamOperation("identity.auth.oauth.link.start", Permission = false)]
    public Task<OAuthStartResponse> StartOAuthLinkAsync(Guid userId, string provider, string redirectUri, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => StartOAuthLinkCoreAsync(userId, provider, redirectUri, token),
            new ServiceOperationExecutionOptions { EntityId = userId, PermissionEnabled = false },
            ct);

    private Task<OAuthStartResponse> StartOAuthLinkCoreAsync(Guid userId, string provider, string redirectUri, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        return StartOAuthInternalAsync(provider, redirectUri, userId);
    }

    [IBeamOperation("identity.auth.oauth.link", Permission = false)]
    public async Task LinkOAuthAsync(Guid userId, OAuthCallbackRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => LinkOAuthCoreAsync(userId, request, token),
            new ServiceOperationExecutionOptions { EntityId = userId, PermissionEnabled = false },
            ct).ConfigureAwait(false);

    private async Task LinkOAuthCoreAsync(Guid userId, OAuthCallbackRequest request, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var normalizedProvider = request.Provider.Trim().ToLowerInvariant();
        var providerOptions = GetProviderOptions(normalizedProvider);
        var stateRecord = GetAndValidateState(request, normalizedProvider, requireLink: true);
        if (!stateRecord.LinkUserId.HasValue || stateRecord.LinkUserId.Value != userId)
            throw new IdentityValidationException("OAuth link state does not match authenticated user.");

        var accessToken = await ExchangeCodeAsync(providerOptions, request.Code.Trim(), stateRecord.CodeVerifier, request.RedirectUri.Trim(), ct);
        var externalUser = await GetUserInfoAsync(normalizedProvider, providerOptions, accessToken, ct);
        await _externalLogins.LinkAsync(userId, normalizedProvider, externalUser.ProviderUserId, externalUser.Email, ct);
    }

    [IBeamOperation("identity.auth.oauth.unlink", Permission = false)]
    public async Task UnlinkOAuthAsync(Guid userId, string provider, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UnlinkOAuthCoreAsync(userId, provider, token),
            new ServiceOperationExecutionOptions { EntityId = userId, PermissionEnabled = false },
            ct).ConfigureAwait(false);

    private async Task UnlinkOAuthCoreAsync(Guid userId, string provider, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(provider))
            throw new IdentityValidationException("Provider is required.");

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        await _externalLogins.UnlinkAsync(userId, normalizedProvider, ct);
    }

    [IBeamOperation("identity.auth.oauth.linked.list", Permission = false)]
    public async Task<IReadOnlyList<LinkedOAuthProvider>> GetLinkedProvidersAsync(Guid userId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetLinkedProvidersCoreAsync(userId, token),
            new ServiceOperationExecutionOptions { EntityId = userId, PermissionEnabled = false },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<LinkedOAuthProvider>> GetLinkedProvidersCoreAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        var links = await _externalLogins.GetByUserAsync(userId, ct);
        return links
            .Select(x => new LinkedOAuthProvider(x.Provider, x.Email))
            .Distinct()
            .ToList();
    }

    private Task<OAuthStartResponse> StartOAuthInternalAsync(string provider, string redirectUri, Guid? linkUserId)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new IdentityValidationException("Provider is required.");
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new IdentityValidationException("RedirectUri is required.");

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var providerOptions = GetProviderOptions(normalizedProvider);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(_oauthOptions.CurrentValue.StateTtlMinutes, 1));
        var state = CreateRandomBase64Url(32);
        var codeVerifier = CreateRandomBase64Url(48);
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var cacheRecord = new OAuthStateRecord(normalizedProvider, redirectUri.Trim(), codeVerifier, expiresAt, linkUserId);
        _cache.Set(CacheKey(state), cacheRecord, expiresAt);

        var authorizationUrl = BuildAuthorizationUrl(providerOptions, normalizedProvider, redirectUri, state, codeChallenge);
        return Task.FromResult(new OAuthStartResponse(normalizedProvider, authorizationUrl, state, expiresAt));
    }

    [IBeamOperation("identity.auth.oauth.complete", Permission = false)]
    public async Task<AuthResultResponse> CompleteOAuthAsync(OAuthCallbackRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CompleteOAuthCoreAsync(request, token),
            new ServiceOperationExecutionOptions { PermissionEnabled = false },
            ct).ConfigureAwait(false);

    private async Task<AuthResultResponse> CompleteOAuthCoreAsync(OAuthCallbackRequest request, CancellationToken ct)
    {
        var traceId = ResolveTraceId();
        var loginAttempt = new LoginAttemptedEvent
        {
            Method = "oauth",
            Identifier = $"{request.Provider?.Trim().ToLowerInvariant()}:{request.State?.Trim()}",
            TraceId = traceId,
            CorrelationId = request.State?.Trim(),
            CausationId = request.State?.Trim()
        };
        loginAttempt.Metadata["idempotencyKey"] = $"{LoginAttemptedEvent.TypeName}:oauth:{request.Provider}:{request.State}";
        await InvokeLifecycleAndPublishAsync(loginAttempt, (hook, evt, token) => hook.OnBeforeLoginAsync(evt, token), ct);

        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Provider))
            throw new IdentityValidationException("Provider is required.");
        if (string.IsNullOrWhiteSpace(request.State))
            throw new IdentityValidationException("State is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new IdentityValidationException("Code is required.");
        if (string.IsNullOrWhiteSpace(request.RedirectUri))
            throw new IdentityValidationException("RedirectUri is required.");

        var normalizedProvider = request.Provider.Trim().ToLowerInvariant();
        var providerOptions = GetProviderOptions(normalizedProvider);
        var stateRecord = GetAndValidateState(request, normalizedProvider, requireLink: false);
        if (stateRecord.LinkUserId.HasValue)
            throw new IdentityValidationException("OAuth state is for linking, not sign-in.");

        var accessToken = await ExchangeCodeAsync(providerOptions, request.Code.Trim(), stateRecord.CodeVerifier, request.RedirectUri.Trim(), ct);
        var externalUser = await GetUserInfoAsync(normalizedProvider, providerOptions, accessToken, ct);

        if (string.IsNullOrWhiteSpace(externalUser.Email))
            throw new IdentityValidationException("OAuth provider did not return an email address.");
        if (!externalUser.EmailVerified)
            throw new IdentityValidationException("OAuth email must be verified.");

        var normalizedEmail = externalUser.Email.Trim().ToLowerInvariant();
        IdentityUser? user = null;
        var link = await _externalLogins.FindByProviderAsync(normalizedProvider, externalUser.ProviderUserId, ct);
        if (link is not null)
            user = await _users.FindByIdAsync(link.UserId, ct);

        user ??= await _users.FindByEmailAsync(normalizedEmail, ct);
        var createdNewUser = false;

        if (user is null)
        {
            var preCreate = new AuthUserCreateRequestedEvent
            {
                NormalizedEmail = normalizedEmail,
                CorrelationId = request.State?.Trim(),
                CausationId = request.State?.Trim(),
                TraceId = traceId
            };
            preCreate.Metadata["idempotencyKey"] = $"{AuthUserCreateRequestedEvent.TypeName}:{normalizedEmail}";
            await InvokeLifecycleAndPublishAsync(preCreate, (hook, evt, token) => hook.OnBeforeAuthUserCreateAsync(evt, token), ct);

            var createResult = await _users.CreateAsync(new RegisterUserRequest(
                Email: normalizedEmail,
                PhoneNumber: null,
                Password: string.Empty,
                DisplayName: externalUser.DisplayName), ct);

            if (!createResult.Succeeded || createResult.User is null)
                throw new IdentityValidationException("User creation failed.", createResult.Errors);

            user = createResult.User with { DisplayName = externalUser.DisplayName ?? createResult.User.DisplayName };
            createdNewUser = true;

            var created = new AuthUserCreatedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                NormalizedEmail = normalizedEmail,
                CorrelationId = request.State?.Trim(),
                CausationId = request.State?.Trim(),
                TraceId = traceId
            };
            created.Metadata["idempotencyKey"] = $"{AuthUserCreatedEvent.TypeName}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(created, (hook, evt, token) => hook.OnAuthUserCreatedAsync(evt, token), ct);
            await OnUserCreatedExtensionAsync(user, null, request.State?.Trim(), request.State?.Trim(), traceId, created.Metadata, ct)
                .ConfigureAwait(false);
        }

        await _externalLogins.LinkAsync(user.UserId, normalizedProvider, externalUser.ProviderUserId, normalizedEmail, ct);
        await _users.SetEmailConfirmedAsync(user.UserId, true, ct);
        var result = await BuildAuthResultAsync(user, createdNewUser, normalizedProvider, ct);
        await EmitLoginSucceededAsync("oauth", user.UserId, result.Token is null ? null : GetTenantIdFromTokenClaims(result), result.RequiresTenantSelection, request.State?.Trim(), traceId, ct);
        return result;
    }

    private OAuthStateRecord GetAndValidateState(OAuthCallbackRequest request, string normalizedProvider, bool requireLink)
    {
        var state = request.State.Trim();
        if (!_cache.TryGetValue(CacheKey(state), out OAuthStateRecord? stateRecord) || stateRecord is null)
            throw new IdentityValidationException("OAuth state is invalid or expired.");
        _cache.Remove(CacheKey(state));

        if (!string.Equals(stateRecord.Provider, normalizedProvider, StringComparison.Ordinal))
            throw new IdentityValidationException("OAuth provider mismatch.");
        if (!string.Equals(stateRecord.RedirectUri, request.RedirectUri.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("OAuth redirect URI mismatch.");
        if (stateRecord.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new IdentityValidationException("OAuth state has expired.");
        if (requireLink && !stateRecord.LinkUserId.HasValue)
            throw new IdentityValidationException("OAuth state is not for link flow.");

        return stateRecord;
    }

    private OAuthProviderOptions GetProviderOptions(string provider)
    {
        var options = _oauthOptions.CurrentValue;
        if (!options.Providers.TryGetValue(provider, out var providerOptions) || providerOptions is null || !providerOptions.Enabled)
            throw new IdentityValidationException($"OAuth provider '{provider}' is not enabled.");

        if (string.IsNullOrWhiteSpace(providerOptions.ClientId) ||
            string.IsNullOrWhiteSpace(providerOptions.ClientSecret) ||
            string.IsNullOrWhiteSpace(providerOptions.AuthorizationEndpoint) ||
            string.IsNullOrWhiteSpace(providerOptions.TokenEndpoint) ||
            string.IsNullOrWhiteSpace(providerOptions.UserInfoEndpoint))
        {
            throw new IdentityValidationException($"OAuth provider '{provider}' is not fully configured.");
        }

        return providerOptions;
    }

    private static string BuildAuthorizationUrl(
        OAuthProviderOptions provider,
        string providerName,
        string redirectUri,
        string state,
        string codeChallenge)
    {
        var scope = string.IsNullOrWhiteSpace(provider.Scope) ? "openid profile email" : provider.Scope;
        var endpoint = provider.AuthorizationEndpoint.Trim();
        var separator = endpoint.Contains('?') ? "&" : "?";

        var prompt = providerName is "google" or "microsoft" ? "&prompt=select_account" : string.Empty;
        return $"{endpoint}{separator}response_type=code&client_id={Uri.EscapeDataString(provider.ClientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256{prompt}";
    }

    private async Task<string> ExchangeCodeAsync(
        OAuthProviderOptions provider,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = provider.ClientId,
                ["client_secret"] = provider.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            })
        };

        using var response = await client.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new IdentityProviderException($"OAuth token exchange failed ({(int)response.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("access_token", out var accessTokenEl))
            throw new IdentityProviderException("OAuth token response did not include access_token.");
        var accessToken = accessTokenEl.GetString();
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new IdentityProviderException("OAuth access_token was empty.");

        return accessToken;
    }

    private async Task<ExternalOAuthUser> GetUserInfoAsync(
        string provider,
        OAuthProviderOptions providerOptions,
        string accessToken,
        CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, providerOptions.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new IdentityProviderException($"OAuth userinfo failed ({(int)response.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var providerUserId = GetString(root, "sub") ?? GetString(root, "id");
        var email = GetString(root, "email");
        var displayName = GetString(root, "name");
        var emailVerified = GetBool(root, "email_verified");

        if (provider == "microsoft")
        {
            email ??= GetString(root, "preferred_username");
            emailVerified = true;
        }

        if (string.IsNullOrWhiteSpace(providerUserId))
            throw new IdentityProviderException("OAuth userinfo missing subject identifier.");

        return new ExternalOAuthUser(providerUserId, email ?? string.Empty, emailVerified, displayName);
    }

    private async Task<AuthResultResponse> BuildAuthResultAsync(
        IdentityUser user,
        bool createdNewUser,
        string provider,
        CancellationToken ct)
    {
        var traceId = ResolveTraceId();
        var activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

        activeTenants = await ApplyTenantProvisioningPolicyAsync(user, createdNewUser, activeTenants, traceId, ct);
        await EnsureTenantExtensionsForUserAsync(user.UserId, TenantExtensionOperations.Ensure, null, null, traceId, ct);

        var policyTenantId = ResolveEffectiveTenantId();
        if (policyTenantId.HasValue)
        {
            var tenant = activeTenants.FirstOrDefault(t => t.TenantId == policyTenantId.Value)
                ?? throw new IdentityValidationException($"User is not linked to tenant '{policyTenantId.Value:D}'.");
            activeTenants = new List<TenantInfo> { tenant };
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user, provider);
            claims.Add(new ClaimItem("tid", tenant.TenantId.ToString("D")));
            foreach (var role in tenant.Roles.Where(r => !string.IsNullOrWhiteSpace(r)))
                claims.Add(new ClaimItem("role", role));
            if (tenant.RoleIds is not null)
            {
                foreach (var roleId in tenant.RoleIds.Where(x => x != Guid.Empty).Distinct())
                    claims.Add(new ClaimItem("rid", roleId.ToString("D")));
            }
            await EnsureUserExtensionAsync(user, tenant.TenantId, UserExtensionOperations.Login, null, null, traceId, ct)
                .ConfigureAwait(false);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token, createdNewUser);
        }

        var defaultTenantId = await _tenants.GetDefaultTenantIdAsync(user.UserId, ct);
        if (defaultTenantId.HasValue)
        {
            var defaultTenant = activeTenants.FirstOrDefault(x => x.TenantId == defaultTenantId.Value);
            if (defaultTenant is not null)
            {
                var claims = BuildBaseClaims(user, provider);
                claims.Add(new ClaimItem("tid", defaultTenant.TenantId.ToString("D")));
                foreach (var role in defaultTenant.Roles.Where(r => !string.IsNullOrWhiteSpace(r)))
                    claims.Add(new ClaimItem("role", role));
                if (defaultTenant.RoleIds is not null)
                {
                    foreach (var roleId in defaultTenant.RoleIds.Where(x => x != Guid.Empty).Distinct())
                        claims.Add(new ClaimItem("rid", roleId.ToString("D")));
                }
                await EnsureUserExtensionAsync(user, defaultTenant.TenantId, UserExtensionOperations.Login, null, null, traceId, ct)
                    .ConfigureAwait(false);
                var token = await _tokens.CreateAccessTokenAsync(user.UserId, defaultTenant.TenantId, claims, ct);
                return AuthResultResponse.WithToken(token, createdNewUser);
            }
        }

        var preClaims = BuildBaseClaims(user, provider);
        preClaims.Add(new ClaimItem("pt", "1"));
        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants, createdNewUser);
    }

    private async Task<List<TenantInfo>> ApplyTenantProvisioningPolicyAsync(
        IdentityUser user,
        bool createdNewUser,
        List<TenantInfo> activeTenants,
        string? traceId,
        CancellationToken ct)
    {
        var options = _tenantProvisioningOptions.Value;
        var configuredTenantId = ResolveEffectiveTenantId();

        if (options.Mode == TenantProvisioningMode.RequireExistingTenant)
        {
            if (configuredTenantId.HasValue)
            {
                if (activeTenants.Any(t => t.TenantId == configuredTenantId.Value))
                    return activeTenants;

                throw new IdentityValidationException($"User is not linked to required tenant '{configuredTenantId.Value:D}'.");
            }

            if (activeTenants.Count == 0)
                throw new IdentityValidationException("User does not have an active tenant membership.");

            return activeTenants;
        }

        if (options.Mode == TenantProvisioningMode.UseDefaultTenant)
        {
            var defaultTenantId = configuredTenantId ?? throw new IdentityValidationException("Default tenant id is required.");
            if (activeTenants.Any(t => t.TenantId == defaultTenantId))
                return activeTenants;

            if (!options.AutoLinkUserToDefaultTenant)
                throw new IdentityValidationException($"User is not linked to default tenant '{defaultTenantId:D}'.");

            await _tenantProvisioning.EnsureUserTenantMembershipAsync(
                defaultTenantId,
                user.UserId,
                roleNames: options.AutoLinkRoleNames,
                setAsDefault: true,
                ct: ct);

            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == defaultTenantId))
                throw new IdentityProviderException("Default tenant membership could not be resolved after linking.");

            return activeTenants;
        }

        if (createdNewUser || activeTenants.Count == 0)
        {
            var preTenant = new TenantCreateRequestedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                SuggestedTenantName = string.IsNullOrWhiteSpace(user.Email) ? null : $"{user.Email.Split('@')[0]}'s Workspace",
                TraceId = traceId
            };
            preTenant.Metadata["idempotencyKey"] = $"{TenantCreateRequestedEvent.TypeName}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(preTenant, (hook, evt, token) => hook.OnBeforeTenantCreateAsync(evt, token), ct);

            var createdTenantId = await _tenantProvisioning.CreateTenantForNewUserAsync(user.UserId, user.Email, ct);
            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == createdTenantId))
                throw new IdentityProviderException("Tenant provisioning completed but membership could not be resolved.");

            var createdTenant = activeTenants.FirstOrDefault(t => t.TenantId == createdTenantId);
            var tenantCreated = new TenantCreatedEvent
            {
                TenantId = createdTenantId,
                TenantName = createdTenant?.Name,
                Status = IdentityTenantStatuses.Active,
                TraceId = traceId
            };
            tenantCreated.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{createdTenantId:D}";
            await InvokeLifecycleAndPublishAsync(tenantCreated, (hook, evt, token) => hook.OnTenantCreatedAsync(evt, token), ct);

            if (createdTenant is not null)
            {
                await _tenantExtensions.OnTenantCreatedAsync(
                    IdentityTenant.FromTenantInfo(createdTenant, DateTimeOffset.UtcNow),
                    TenantExtensionContext.Create(
                        TenantExtensionOperations.Created,
                        user.UserId,
                        traceId: traceId,
                        metadata: tenantCreated.Metadata),
                    ct).ConfigureAwait(false);
            }

            var preLink = new TenantUserLinkRequestedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                TraceId = traceId
            };
            preLink.Metadata["idempotencyKey"] = $"{TenantUserLinkRequestedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(preLink, (hook, evt, token) => hook.OnBeforeTenantUserLinkAsync(evt, token), ct);

            var linked = new TenantUserLinkedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                Role = createdTenant?.Roles?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)),
                UserTenantId = $"{createdTenantId:D}|{user.UserId:D}",
                TraceId = traceId
            };
            linked.Metadata["idempotencyKey"] = $"{TenantUserLinkedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(linked, (hook, evt, token) => hook.OnTenantUserLinkedAsync(evt, token), ct);
        }

        return activeTenants;
    }

    private async Task<List<TenantInfo>> GetActiveTenantsForUserAsync(Guid userId, CancellationToken ct)
    {
        var tenants = await _tenantInfoResolver
            .EnrichAsync(await _tenants.GetTenantsForUserAsync(userId, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

        return tenants.Where(t => t.IsActive).ToList();
    }

    private async Task EnsureTenantExtensionsForUserAsync(
        Guid userId,
        string operation,
        string? correlationId,
        string? causationId,
        string? traceId,
        CancellationToken ct)
    {
        var identityTenantsTask = _tenants.GetTenantsForUserAsync(userId, ct);
        if (identityTenantsTask is null)
            return;

        var identityTenants = await identityTenantsTask.ConfigureAwait(false);
        if (identityTenants is null)
            return;

        foreach (var tenant in identityTenants.Where(t => t.IsActive))
        {
            await _tenantExtensions.EnsureExtensionAsync(
                IdentityTenant.FromTenantInfo(tenant),
                TenantExtensionContext.Create(operation, userId, correlationId, causationId, traceId),
                ct).ConfigureAwait(false);
        }
    }

    private Task OnUserCreatedExtensionAsync(
        IdentityUser user,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        string? traceId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
        => _userExtensions.OnUserCreatedAsync(
            user,
            CreateUserExtensionContext(user, UserExtensionOperations.Created, tenantId, correlationId, causationId, traceId, metadata),
            ct);

    private Task EnsureUserExtensionAsync(
        IdentityUser user,
        Guid? tenantId,
        string operation,
        string? correlationId,
        string? causationId,
        string? traceId,
        CancellationToken ct)
        => _userExtensions.EnsureExtensionAsync(
            user,
            CreateUserExtensionContext(user, operation, tenantId, correlationId, causationId, traceId, null),
            ct);

    private static UserExtensionContext CreateUserExtensionContext(
        IdentityUser user,
        string operation,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        string? traceId,
        IReadOnlyDictionary<string, string>? metadata)
        => UserExtensionContext.Create(
            operation,
            user.UserId,
            tenantId,
            user.Email,
            user.PhoneNumber,
            user.DisplayName,
            correlationId: correlationId,
            causationId: causationId,
            traceId: traceId,
            metadata: metadata);

    private Guid? ResolveEffectiveTenantId()
    {
        var options = _tenantProvisioningOptions.Value;
        return options.Mode == TenantProvisioningMode.UseDefaultTenant ||
               (options.Mode == TenantProvisioningMode.RequireExistingTenant && options.DefaultTenantId.HasValue)
            ? options.DefaultTenantId
            : null;
    }

    private static List<ClaimItem> BuildBaseClaims(IdentityUser user, string provider)
    {
        var claims = new List<ClaimItem>
        {
            new("sub", user.UserId.ToString("D")),
            new("uid", user.UserId.ToString("D")),
            new("auth_provider", provider)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new ClaimItem("email", user.Email));
        return claims;
    }

    private async Task InvokeLifecycleAndPublishAsync<TEvent>(
        TEvent evt,
        Func<IAuthLifecycleHook, TEvent, CancellationToken, Task> hookInvoker,
        CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await hookInvoker(_lifecycleHook, evt, ct);
            await _eventPublisher.PublishAsync(evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit auth lifecycle event {EventType}. EventId={EventId}", evt.EventType, evt.EventId);
            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private Task EmitLoginSucceededAsync(
        string method,
        Guid userId,
        Guid? tenantId,
        bool requiresTenantSelection,
        string? correlationId,
        string? traceId,
        CancellationToken ct)
    {
        var evt = new LoginSucceededEvent
        {
            Method = method,
            AuthUserId = userId.ToString("D"),
            TenantId = tenantId,
            RequiresTenantSelection = requiresTenantSelection,
            CorrelationId = correlationId,
            CausationId = correlationId,
            TraceId = traceId
        };
        evt.Metadata["idempotencyKey"] =
            $"{LoginSucceededEvent.TypeName}:{method}:{tenantId?.ToString("D") ?? "pretenant"}:{userId:D}";
        return InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnLoginSucceededAsync(e, token), ct);
    }

    private static Guid? GetTenantIdFromTokenClaims(AuthResultResponse response)
    {
        var tid = response.Token?.Claims?.FirstOrDefault(c => string.Equals(c.Type, "tid", StringComparison.OrdinalIgnoreCase))?.Value;
        return Guid.TryParse(tid, out var parsed) ? parsed : null;
    }

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;
        return current.TraceId != default ? current.TraceId.ToString() : current.Id;
    }

    private static string CacheKey(string state) => $"oauth_state:{state}";

    private static string CreateRandomBase64Url(int byteLength)
    {
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool GetBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return false;
        return value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b) && b);
    }

    private sealed record OAuthStateRecord(string Provider, string RedirectUri, string CodeVerifier, DateTimeOffset ExpiresAt, Guid? LinkUserId);
    private sealed record ExternalOAuthUser(string ProviderUserId, string Email, bool EmailVerified, string? DisplayName);
}
