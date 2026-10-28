-- Hearthsheet server-side guardrails. Run once after 0001 in the Supabase SQL editor (see docs/CLOUD_SYNC.md).
-- Then enable the hook under Authentication -> Hooks -> Before User Created.

-- Grants: exactly what 0001 lists, independent of Supabase's default privileges (which differ between projects).
revoke all on public.characters from anon, authenticated;
grant select, insert, update on public.characters to authenticated;
grant select on public.characters to anon;   -- keep-alive; RLS returns []

-- 1 MB per document, about 60x a real 17 KB character.
alter table public.characters add constraint characters_data_size
  check (data is null or octet_length(data::text) <= 1048576);

-- At most 100 rows per user, live plus tombstones. The lock serializes concurrent inserts by one user.
create function public.characters_cap() returns trigger
language plpgsql set search_path = '' as $$
begin
  perform pg_advisory_xact_lock(hashtextextended(new.user_id::text, 0));
  if (select count(*) from public.characters where user_id = new.user_id) >= 100 then
    raise exception 'Character limit reached (100). Delete some characters or ask Beau.' using errcode = 'P0001';
  end if;
  return new;
end $$;

create trigger characters_cap before insert on public.characters
  for each row execute function public.characters_cap();

-- Sign-up allowlist, in a schema the Data API doesn't expose.
create schema if not exists private;
create table private.signup_allowlist (
  email    text primary key,
  added_at timestamptz not null default now(),
  source   text not null
);
insert into private.signup_allowlist (email, source)
  select lower(trim(email)), 'existing' from auth.users where email is not null and trim(email) <> ''
  on conflict do nothing;

-- Before User Created auth hook: listed emails pass; the first 10 are added automatically; everyone else is refused.
create function public.hook_before_user_created(event jsonb) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare
  v_email text := lower(trim(event->'user'->>'email'));
begin
  if v_email is null or v_email = '' then
    return jsonb_build_object('error', jsonb_build_object('http_code', 403, 'message', 'Sign-ups are closed. Ask Beau to add your email.'));
  end if;

  perform pg_advisory_xact_lock(hashtextextended('signup_allowlist', 0));

  if exists (select 1 from private.signup_allowlist where email = v_email) then
    return '{}'::jsonb;
  end if;

  if (select count(*) from private.signup_allowlist) < 10 then
    insert into private.signup_allowlist (email, source) values (v_email, 'auto');
    return '{}'::jsonb;
  end if;

  return jsonb_build_object('error', jsonb_build_object('http_code', 403, 'message', 'Sign-ups are closed. Ask Beau to add your email.'));
end $$;

grant usage on schema private to supabase_auth_admin;
grant select, insert on private.signup_allowlist to supabase_auth_admin;
grant execute on function public.hook_before_user_created to supabase_auth_admin;
revoke execute on function public.hook_before_user_created from public, anon, authenticated;
revoke all on schema private from anon, authenticated;
