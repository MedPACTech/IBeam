import {
  BadgeCheck,
  BookOpen,
  Fingerprint,
  KeyRound,
  Layers3,
  LockKeyhole,
  Map,
  Network,
  PlugZap,
  ShieldCheck,
  Table2,
  UsersRound,
  Wrench
} from "lucide-react";
import type { ComponentType, ReactElement } from "react";
import { OverviewPage } from "../features/identity/OverviewPage";
import { WorkflowPage } from "../features/identity/WorkflowPage";

export type DemoRoute = {
  path: string;
  label: string;
  icon: ComponentType<{ size?: number }>;
  element: ReactElement;
};

export const demoRoutes: DemoRoute[] = [
  {
    path: "/",
    label: "Start Here",
    icon: BookOpen,
    element: <OverviewPage />
  },
  {
    path: "/composition",
    label: "API Composition",
    icon: PlugZap,
    element: (
      <WorkflowPage
        title="API Composition"
        summary="Wire the reusable identity API into a host application with services, controllers, JWT auth, Azure Table stores, communications, and authorization policies."
        endpoints={["builder.Services.AddIBeamIdentityApi(...)", "builder.Services.AddIBeamIdentityApiControllers()"]}
        snippetKey="apiComposition"
      />
    )
  },
  {
    path: "/registration",
    label: "Registration",
    icon: Fingerprint,
    element: (
      <WorkflowPage
        title="Email Password Registration"
        summary="Start and complete verified email/password registration, then inspect the created user, tenant membership, token, and table rows."
        endpoints={["POST /api/auth/start-email-password-registration", "POST /api/auth/complete-email-password-registration"]}
        snippetKey="registration"
      />
    )
  },
  {
    path: "/otp",
    label: "OTP Login",
    icon: BadgeCheck,
    element: (
      <WorkflowPage
        title="OTP Login"
        summary="Exercise SMS or email OTP challenge creation, verification, optional auto-provisioning, and token issuance."
        endpoints={["POST /api/auth/startotp", "POST /api/auth/completeotp"]}
        snippetKey="otp"
      />
    )
  },
  {
    path: "/password-login",
    label: "Password Login",
    icon: KeyRound,
    element: (
      <WorkflowPage
        title="Password Login"
        summary="Authenticate with email/password, handle two-factor-required responses, and capture access and refresh tokens."
        endpoints={["POST /api/auth/password-login"]}
        snippetKey="passwordLogin"
      />
    )
  },
  {
    path: "/two-factor",
    label: "Two-Factor",
    icon: LockKeyhole,
    element: (
      <WorkflowPage
        title="Two-Factor Auth"
        summary="Configure a preferred 2FA method, complete setup, complete 2FA login, or disable it from an authenticated session."
        endpoints={[
          "POST /api/auth/2fa/setup/start",
          "POST /api/auth/2fa/setup/complete",
          "POST /api/auth/2fa/complete-login",
          "POST /api/auth/2fa/disable",
          "POST /api/auth/2fa/method"
        ]}
        snippetKey="twoFactor"
      />
    )
  },
  {
    path: "/oauth",
    label: "OAuth",
    icon: Network,
    element: (
      <WorkflowPage
        title="OAuth"
        summary="Start OAuth, complete provider callback handling, link providers to the current user, list linked providers, and unlink providers."
        endpoints={[
          "GET /api/auth/oauth/start",
          "POST /api/auth/oauth/complete",
          "GET /api/auth/oauth/link/start",
          "POST /api/auth/oauth/link/complete",
          "GET /api/auth/oauth/linked",
          "POST /api/auth/oauth/unlink"
        ]}
        snippetKey="oauth"
      />
    )
  },
  {
    path: "/tenants",
    label: "Tenants",
    icon: Layers3,
    element: (
      <WorkflowPage
        title="Tenant Provisioning"
        summary="Compare AutoCreateTenantForNewUser, UseDefaultTenant, and RequireExistingTenant behavior during auth flows."
        endpoints={["IBeam:Identity:TenantProvisioning"]}
        snippetKey="tenantProvisioning"
      />
    )
  },
  {
    path: "/tokens-sessions",
    label: "Tokens & Sessions",
    icon: ShieldCheck,
    element: (
      <WorkflowPage
        title="Tokens & Sessions"
        summary="Decode access tokens, refresh tokens, inspect auth sessions, and revoke sessions."
        endpoints={["POST /api/auth/refresh", "GET /api/auth/sessions", "POST /api/auth/sessions/revoke"]}
        snippetKey="sessions"
      />
    )
  },
  {
    path: "/roles",
    label: "Roles",
    icon: UsersRound,
    element: (
      <WorkflowPage
        title="Tenant Roles"
        summary="Create tenant-scoped roles, update them, grant roles to users, revoke roles, and inspect user role membership."
        endpoints={[
          "GET /api/tenants/{tenantId}/roles",
          "POST /api/tenants/{tenantId}/roles",
          "POST /api/tenants/{tenantId}/roles/grant",
          "POST /api/tenants/{tenantId}/roles/revoke"
        ]}
        snippetKey="roles"
      />
    )
  },
  {
    path: "/permissions",
    label: "Permissions",
    icon: Map,
    element: (
      <WorkflowPage
        title="Permission Mappings"
        summary="Expose a permission catalog and map permission names or IDs to tenant role names and role IDs."
        endpoints={[
          "GET /api/tenants/{tenantId}/permissions/catalog",
          "GET /api/tenants/{tenantId}/permissions/mappings",
          "PUT /api/tenants/{tenantId}/permissions/mappings/by-name"
        ]}
        snippetKey="permissions"
      />
    )
  },
  {
    path: "/storage",
    label: "Azure Tables",
    icon: Table2,
    element: (
      <WorkflowPage
        title="Azure Table Storage"
        summary="Inspect the identity table schema, configured prefix, connection string resolution, and records affected by each workflow."
        endpoints={["IBeam:Identity:AzureTable", "UseDevelopmentStorage=true"]}
        snippetKey="azureTables"
      />
    )
  },
  {
    path: "/troubleshooting",
    label: "Troubleshooting",
    icon: Wrench,
    element: (
      <WorkflowPage
        title="Troubleshooting"
        summary="Diagnose Azurite, JWT, tenant provisioning, schema, credential, and authorization failures from symptoms to fixes."
        endpoints={["401", "403", "validation errors", "schema errors"]}
        snippetKey="troubleshooting"
      />
    )
  }
];
