-- For a DEDICATED e2e database only: removes the season the roster-draft e2e started and the two administrators it added.
with doomed as (select "Id" from league.seasons where "Name" = '2027 E2E Swan League')
delete from picks.rosters where "SeasonId" in (select "Id" from doomed);
delete from schedule.games where "WeekId" in (select w."Id" from schedule.weeks w join league.seasons s on s."Id" = w."SeasonId" where s."Name" = '2027 E2E Swan League');
delete from schedule.weeks where "SeasonId" in (select "Id" from league.seasons where "Name" = '2027 E2E Swan League');
delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "Name" = '2027 E2E Swan League');
delete from league.memberships where "SeasonId" in (select "Id" from league.seasons where "Name" = '2027 E2E Swan League');
delete from league.seasons where "Name" = '2027 E2E Swan League';
delete from identity.sessions where "UserId" in ('00000000-0000-0000-0000-0000000009c1', '00000000-0000-0000-0000-0000000009c2');
delete from identity.users where "Id" in ('00000000-0000-0000-0000-0000000009c1', '00000000-0000-0000-0000-0000000009c2');
