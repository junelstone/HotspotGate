-- Stone's Hotspot licence server (Supabase). Same data as src/StonesHotspot.LicenseServer/ServerStore.cs.
-- Row level security is on with no policy: only the Edge Function (service role) can read or write these tables.
-- The owner reads them in the Supabase dashboard (Table Editor).

create table public.licences (
    id          bigint primary key,
    customer    text not null,
    machine_id  text not null,
    edition     text not null,
    issued_on   date not null,
    expires_on  date not null,
    code        text not null,
    revoked_at  timestamptz
);

create table public.trials (
    machine_id    text primary key,
    first_run     timestamptz not null,
    registered_at timestamptz not null,
    last_seen     timestamptz not null,
    version       text
);

create table public.checkins (
    id          bigint generated always as identity primary key,
    licence_id  bigint,
    machine_id  text not null,
    kind        text not null,
    result      text not null,
    at          timestamptz not null default now(),
    ip          text,
    version     text
);
create index checkins_at on public.checkins (at desc);
create index checkins_licence on public.checkins (licence_id, at desc);

-- Latest hotspot version, edited by the owner in the Table Editor after uploading an installer (GitHub Release).
-- Leave version empty to publish none. The Edge Function signs it with the server key.
create table public.update_manifest (
    id           int primary key default 1 check (id = 1),
    version      text not null default '',
    url          text not null default '',
    sha256       text not null default '',
    notes        text,
    published_at timestamptz not null default now()
);
insert into public.update_manifest (id) values (1);

alter table public.licences enable row level security;
alter table public.trials enable row level security;
alter table public.checkins enable row level security;
alter table public.update_manifest enable row level security;

-- Registers a trial; the earliest first run ever seen for this PC is kept and returned.
create function public.register_trial(p_machine_id text, p_first_run timestamptz, p_now timestamptz, p_version text)
returns timestamptz
language sql
as $$
    insert into public.trials (machine_id, first_run, registered_at, last_seen, version)
    values (p_machine_id, p_first_run, p_now, p_now, p_version)
    on conflict (machine_id) do update
        set first_run = least(trials.first_run, excluded.first_run),
            last_seen = excluded.last_seen,
            version   = excluded.version
    returning first_run;
$$;

-- Adds or replaces licences (LicenseManager upload, or a code first seen at activation). A revocation is never undone.
create function public.upsert_licences(p_licences jsonb)
returns int
language sql
as $$
    with rows as (
        insert into public.licences (id, customer, machine_id, edition, issued_on, expires_on, code, revoked_at)
        select x.id, x.customer, x."machineId", x.edition, x."issuedOn", x."expiresOn", x.code, x."revokedAt"
        from jsonb_to_recordset(p_licences) as x(id bigint, customer text, "machineId" text, edition text,
                                                 "issuedOn" date, "expiresOn" date, code text, "revokedAt" timestamptz)
        on conflict (id) do update
            set customer   = excluded.customer,
                machine_id = excluded.machine_id,
                edition    = excluded.edition,
                issued_on  = excluded.issued_on,
                expires_on = excluded.expires_on,
                code       = excluded.code,
                revoked_at = coalesce(excluded.revoked_at, licences.revoked_at)
        returning 1
    )
    select count(*)::int from rows;
$$;

revoke all on function public.register_trial(text, timestamptz, timestamptz, text) from public, anon, authenticated;
revoke all on function public.upsert_licences(jsonb) from public, anon, authenticated;
grant execute on function public.register_trial(text, timestamptz, timestamptz, text) to service_role;
grant execute on function public.upsert_licences(jsonb) to service_role;
