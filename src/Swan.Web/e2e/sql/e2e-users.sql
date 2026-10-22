-- Dedicated browser-test identities. Each independent flow uses its own address because
-- magic-link requests are rate limited per recipient.
insert into identity.users ("Id", "Email", "Name", "IsActive", "Role") values
  ('00000000-0000-0000-0000-0000000009c2', 'other.admin@swan.local', 'E2E Other Admin', true, 0),
  ('00000000-0000-0000-0000-0000000009c3', 'e2e.admin2@swan.local', 'E2E Admin 2', true, 0),
  ('00000000-0000-0000-0000-0000000009c4', 'e2e.admin3@swan.local', 'E2E Admin 3', true, 0)
on conflict do nothing;
