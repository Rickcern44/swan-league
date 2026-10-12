# Swan League App — Product Requirements

Oct 12, 2026 · @Rick

## 1. Overview and scope

The Swan League App replaces the weekly Google form, the master spreadsheet, and the Tuesday lines email with one web app (React PWA) where coaches manage rosters and weekly picks and the commish runs the league.

**Delivery context.** This is first a personal build, run locally or as a deployed test, possibly with a simulated "local season" to shake out bugs. It is pitched to the league only once polished, most likely as a next-season tool. The league-wide design stays in scope so nothing needs rework at pitch time.

**Goals**

- Coaches submit rosters, 3 team picks, and dog picks in-app, and can never miss a deadline silently.
- The commish approves each week's lines, confirms results, and manages members, rulings, and year-end payments from an admin console.
- Scoring, standings, and the four pools are computed by the system and always auditable.
- Every league message (lines posted, reminders, results) goes out by email and push from one place.

**In scope:** accounts and invites, roster, weekly picks, dog picks, odds import with approval, results import with confirmation, scoring, standings for all four pools, playoffs, announcements, notifications, admin console, payments ledger, history import, season rollover.

**Out of scope (v1):** pick-recommendation math or EV formulas, real money movement (ledger only), SMS, the March Madness pool, native mobile apps, and multiple leagues.

**League facts:** $100 buy-in, a $60 main pool, a $20 dog pool, a $10 regular-season pool, and a $10 playoff pool. The league is the Swan League, run by an anonymous commish called "the Swan."

## 2. Decisions and assumptions

Ten decisions are locked; the assumptions below them are mine and need your confirmation.

| # | Decision | Choice |
| --- | --- | --- |
| D1 | Audience | Whole league; replaces the Google form and master spreadsheet |
| D2 | Frontend | React (PWA, installable, web push) |
| D3 | Backend | ASP.NET Core Minimal APIs with vertical slices and DDD-lite layers (Core, Data, Services), plus a separate Worker host |
| D4 | Data and hosting | PostgreSQL via EF Core; .NET Aspire for local orchestration; deployment target deferred |
| D5 | Lines | MVP: the admin enters or pastes lines by hand and approves them before coaches see them; an odds API import (a draft needing the same approval) follows in Milestone 2 |
| D6 | Results | Imported from a scores API in the MVP (BallDontLie first, The Odds API second, behind an adapter); scoring is computed; the admin confirms and can override each week, with manual entry as a fallback |
| D7 | Sign-in | Email magic link, no passwords |
| D8 | Notifications | Email and PWA push |
| D9 | Payments | Ledger only; no money moves through the app |
| D10 | Unresolved rules | Owner-controlled league settings with defaults (see section 4); no general rules engine |

**Answers taken from the rules doc (no longer open)**

- Dog points equal the spread rounded up (Browns +6 pays 6), not the team's division value.
- A tie is not a win and scores zero.
- A top seed's Wild Card bye is compensated with 10 points when that team wins the conference.
- Perfect Week excludes the dog; "Going Perf" includes it.

**Decisions added on Oct 12**

- Infrastructure is deferred: the first goal is a fully working local stack (section 10.3). Hosting, infrastructure as code, and email sending are noted in section 10.4 for later.
- Roster: free pick, several coaches may own the same team, and there are no mid-season swaps.
- Pick locks are per pick: a team pick locks at the earlier of its kickoff and the Sunday 1:00 PM ET deadline, so partial picks are normal.
- Pick visibility depends on your own locks: you see other coaches' locked team picks once your own three team picks are locked, and their locked dogs once your own dog picks are locked, so nobody can change a pick after seeing others'.
- A dog may not be the same team twice in a week, nor two dogs from the same game, in a double-dog week.
- Playoff points follow the rules doc; coaches still pick in the playoffs (details pending, section 12).
- Nicknames show in standings and elsewhere, with an optional flag to show a real name on a profile.
- The master spreadsheet import, covering all seasons, is Milestone 2 and not part of the MVP.
- Theming stays "goofy"; a collaborative design pass for logo and theme comes later in the build.

**Further decisions, Oct 12**

- Lines are entered manually in the MVP (a form plus CSV paste) and automated in Milestone 2; final scores import automatically in the MVP, with admin confirmation.
- Playoff points use the season point value times the round multiplier, and coaches pick in the playoffs (up to 3 from roster teams still alive), per the defaults in section 12.
- Budget: hosting, email, and data together under $10 a month, as close to free as possible.
- The app and email sending use subdomains of cernylabs.com.
- The show-real-name flag is a per-coach setting, off by default.
- Every non-essential notification can be muted per coach, per category and channel, with a master quiet mode; the early-game reminder is sent once.

* The NFL schedule is uploaded by CSV in the MVP; automated schedule import comes with automated lines in Phase 6.
* Subdomains: swan.cernylabs.com for the app and mail.cernylabs.com for email, with DNS managed as code where the host allows.
* Scores provider: BallDontLie first, The Odds API second, both behind the same adapter. The Odds API is also the likely lines source in Phase 6. Sportradar is test-only (section 7, S7).

- Architecture: Core, Data, and Services projects with vertical-slice features, thin minimal endpoints, and a separate Worker host for scheduled jobs (section 10).

* Hosting is decided in Phase 1b, right after the scoring core; a single VPS running the stack in containers is an acceptable outcome.

- A new-season wizard on the admin dashboard starts a blank season (section 6.2) and is built in Phase 2.

**Assumptions to confirm**

- A1: The league has roughly 30 coaches (the buy-in totals imply 30: $1,800 / $60). All use their own email.
- A2: There is no 2026 cutover deadline. The first live use may be a local simulated season, and the league's Google form stays the system of record for 2026 unless you decide otherwise.
- A3: All times display in Eastern Time; deadlines are stored in UTC.
- A4: One league, one active season at a time; every prior season is imported from the master spreadsheet (Milestone 2) and kept read-only.
- A5: The commish identity stays anonymous to coaches; the admin role is a login, not a visible name.

## 3. Roles and permissions

Three roles cover the league; every admin action is written to an audit log.

| Capability | Coach | Admin (commish) | System |
| --- | --- | --- | --- |
| Sign in by magic link | Yes | Yes | n/a |
| Manage own roster (pre-season / draft window) | Yes | Yes, for anyone | n/a |
| Submit and edit own weekly picks before lock | Yes | Yes, for anyone (logged) | n/a |
| Submit dog pick before that game kicks | Yes | Yes, for anyone (logged) | n/a |
| View own picks, scores, standings, history | Yes | Yes | n/a |
| View others' picks | Only under rule R21: locked team picks once your own team picks are locked, locked dogs once your own dogs are locked | Always | n/a |
| Import and edit lines, approve and publish them | No | Yes | Imports drafts |
| Confirm or override results | No | Yes | Imports scores |
| Post announcements and send notifications | No | Yes | Sends scheduled reminders |
| Invite, deactivate, and edit coaches | No | Yes | n/a |
| Change league settings and record rulings | No | Yes | n/a |
| Manage fines, buy-ins, payouts ledger | No | Yes | n/a |
| View audit log | No | Yes | Writes it |

A coach can edit each team pick until the earlier of that team's kickoff and the weekly deadline (Sunday 1:00 PM ET), and a dog pick only until that game kicks off (exact lock rules in section 4). An admin override after lock requires a reason and notifies the affected coach.

## 4. Rules the system enforces

Each rule below is a testable requirement. Rules marked **Setting** are owner-controlled toggles with a default, so a commish ruling never needs a code change.

| ID | Rule | Source | Setting? |
| --- | --- | --- | --- |
| R1 | Each coach has exactly 9 NFL teams on their season roster | Rules doc | No |
| R2 | Each week a coach starts exactly 3 of their 9 teams | Rules doc | No |
| R3 | A team's point value is its 2025 division finish: 1st = 1, 2nd = 2, 3rd = 3, 4th = 4 | Rules doc | No |
| R4 | A started team that wins scores its point value; a loss or tie scores 0 | Rules doc | No |
| R5 | Perfect Week: +3 bonus when all 3 started teams win; the dog is excluded | Rules doc | No |
| R6 | Each team pick locks at the earlier of that team's kickoff and the weekly deadline, Sunday 1:00 PM ET | Rules doc | No |
| R7 | Each week a coach picks 1 underdog (the +spread side); Week 2, Thanksgiving week, and Christmas week allow 2 | Rules doc | No |
| R8 | A dog pick must be submitted before that game's kickoff; Monday-night dogs are allowed until then | Rules doc | No |
| R9 | A dog that wins outright scores its spread rounded up (+6.5 pays 7) | Rules doc | No |
| R10 | A missed team-pick submission is a $5 fine, added to the fine pool | Rules doc | No |
| R11 | Going Perf = all 3 team picks and the dog win in one week; 0fer = all 3 picks and the dog lose | Rules doc | No |
| R12 | Playoff teams earn 3 points for their coaches for qualifying, counted only in the playoff pool | Rules doc | No |
| R13 | Playoff points use the team's season point value (the rules doc says "original seeding") times the round multiplier: Wild Card x2, Divisional x4, Conference x6, Super Bowl x8 | Rules doc | No |
| R14 | The conference winner that held the #1 seed pays 10 points to coaches holding it | Rules doc | No |
| R15 | Dog points count toward the main pool as well as the dog pool | Open | Yes: default Off (dog pool only) |
| R16 | A coach may use the same team as a dog any number of times in a season; the only restriction is no repeated team or game within one double-dog week | Decided | No: fixed |
| R17 | A team may be both a started team and the same week's dog | Open | Yes: default On |
| R18 | Color tiers for tiebreakers map to 1-4 point values (green, blue, yellow, red) | Open | Yes: mapping editable, default 1=green, 2=blue, 3=yellow, 4=red |
| R19 | "Going Perf" and "0fer" records are held by first occurrence, not most occurrences | Open | Yes: default first occurrence |
| R20 | A team whose game kicks off before the Sunday deadline (Thursday, Friday, Saturday, or the Sunday 9:30 AM ET London game) must be picked before that kickoff; the other picks stay open until the deadline | Decided | No: always on |
| R21 | Visibility: a coach sees another coach's locked team picks only after all of their own team picks for that week are locked (in practice, after the weekly deadline), and another coach's locked dogs only after all of their own dog picks for that week are locked. Admins always see everything | Decided | No: always on |

**Lock behavior.** The system computes a lock time for every pick. A team pick locks at the earlier of that team's kickoff and the weekly deadline; a dog pick locks at its own kickoff. A coach can never create or edit a pick after its lock. This allows partial picks: a coach whose roster includes a Thursday team submits that team before Thursday's kickoff, then adds the other two before Sunday at 1:00 PM ET. A week is complete when it holds 3 valid team picks.

**Missed picks.** If a coach has fewer than 3 valid team picks at the weekly deadline, the system flags the week as incomplete, creates a pending $5 fine for the admin to confirm, and scores zero for the missing slots. It never invents a pick for the coach.

**Collisions.** If two of a coach's started teams play each other, both may be started; the system scores the winner and the loser normally. It warns the coach at pick time but does not block.

## 5. Coach features

Every feature has an ID so agents can reference and test it. Priority: **P0** must ship in the first release, **P1** soon after, **P2** nice to have.

| ID | Feature | Requirement | Priority |
| --- | --- | --- | --- |
| C1 | Invite and sign-in | A coach joins from an emailed invite and signs in by magic link; links expire in 15 minutes and are single-use; sessions last 30 days on a device | P0 |
| C2 | Profile | Display name, optional nickname (admin-assigned; shown in standings and feeds), email, notification preferences per channel and event type, and an optional flag to show the coach's real name on their own profile | P0 |
| C3 | Roster | View the 9 teams with 2025 division finish and point value; editable only while the pre-season roster window is open, with no mid-season swaps; free pick, and several coaches may own the same team; the app rejects duplicate teams within one roster and counts other than 9 | P0 |
| C4 | Weekly picks | One screen per week shows all 9 teams with opponent, home or away, kickoff time, approved spread and moneyline, point value, and bye status; the coach saves picks one at a time or all at once, and the week is complete at exactly 3 | P0 |
| C5 | Pick validation | Blocks bye teams, blocks a team whose game has already started, blocks more than 3, flags fewer than 3 as incomplete, warns when two selected teams play each other, shows the lock countdown | P0 |
| C6 | Pick confirmation | After submit the coach sees a receipt and gets an email; picks can be edited until lock; every change is versioned | P0 |
| C7 | Dog pick | Shows the week's underdogs (admin-approved list), spread, kickoff, and points earned if it wins; the coach selects 1 (2 in double-dog weeks); enforces R7 and R8, allows the same dog all season (R16), and in a double-dog week requires two different teams from two different games | P0 |
| C8 | Missing-pick banner | A persistent banner until picks are complete, plus deadline reminders (section 8) | P0 |
| C9 | Live week view | Shows another coach's picks only once that coach's picks are locked and the viewer's matching picks are locked too (R21); until then each coach sees only their own picks and totals, with live game status | P0 |
| C10 | Standings | Four leaderboards (main, dog, regular season, playoffs) with rank, points, wins, and tiebreaker columns; current week and season-to-date | P0 |
| C11 | My history | Week-by-week results for the coach: picks, outcomes, points, bonuses, fines | P0 |
| C12 | League history | Every coach's picks and scores by week, across all imported seasons once Milestone 2 ships (this replaces the master spreadsheet); sortable and exportable to CSV | P1 |
| C13 | Announcements feed | Read commish posts and league messages, with unread markers | P0 |
| C14 | Records and badges | Going Perf and 0fer record boards; past-champion, dog-pool, regular-season, playoff, purped, and JTBL badges next to names | P1 |
| C15 | Rules page | The league rules and the current setting values, readable in-app | P1 |
| C16 | Playoff screen | Shows the coach's playoff teams, qualification points, round multipliers, projected playoff points, and the coach's playoff picks where the round requires them | P1 |

**Key flows**

1. Tuesday: lines are approved and published, the coach is notified, and the weekly picks screen opens.
2. Pick week: the coach opens the picks screen, selects 3 teams and a dog, submits, and receives a receipt; edits are allowed until lock.
3. Lock: early-game picks lock at their kickoff; at the weekly deadline all remaining team picks become read-only, the live week view opens, and missing picks are flagged to the admin.
4. Final: results import, the admin confirms, scores and standings update, and the recap is sent.

## 6. Admin console

The admin console is a separate area, visible only to admins, with a weekly workflow at its center.

| ID | Area | Requirement | Priority |
| --- | --- | --- | --- |
| A1 | Week dashboard | One screen per week showing state (Draft, Lines approved, Open, Locked, Final), deadlines, how many coaches have submitted, who is missing, and the next required admin action | P0 |
| A2 | Lines review | Shows lines per game (spread, moneyline, source: manual, CSV paste, or later API import, and entry time); in the MVP the admin types or pastes them; the admin edits any value, marks games as excluded, and approves per game or in bulk; coaches see nothing until published | P0 |
| A3 | Dog list | Auto-generates the underdog list from approved lines; the admin can add or remove dogs, mark double-dog weeks, and set which dogs qualify (home or away, spread band) | P0 |
| A4 | Publish and lock | Publishing opens picks and notifies coaches; locks are derived from the server clock, so they take effect on time even if a job is late; an admin lock or unlock option is logged | P0 |
| A5 | Results confirmation | Shows imported final scores (with a manual entry fallback) and computed points per coach; the admin confirms, overrides a game result (with reason), or reopens a week; confirming triggers the recap | P0 |
| A6 | Pick overrides | The admin can create or change any coach's picks, before or after lock, with a required reason, an audit entry, and a notification to the coach | P0 |
| A7 | Members | Invite, resend, deactivate, and reactivate coaches; assign nicknames; view each coach's roster | P0 |
| A8 | Rosters | Open and close the pre-season roster window; correct data-entry errors on any roster (logged with a reason); there are no mid-season swaps | P0 |
| A9 | League settings | Toggles for R15, R17, R18, and R19 and the deadline time; each change shows who changed it, when, and the old value | P0 |
| A10 | Rulings log | A free-text log of commish rulings, linked to the setting they changed, visible to coaches on the rules page | P1 |
| A11 | Fines | Pending fines are generated automatically for missed picks; the admin confirms, waives, or adds a manual fine; every fine has a reason | P0 |
| A12 | Payments ledger | Per coach: buy-in owed and paid, fines, and payouts (section 6.1); admin marks each item paid with a date and note | P0 |
| A13 | Announcements | Compose, preview, schedule, and send a message to all coaches or a subset by email, push, or both; pin important posts | P0 |
| A14 | Purped | Each week the admin picks one coach to highlight; shows on the standings page | P2 |
| A15 | History import | Milestone 2, not MVP: import the master spreadsheet for every season (coaches, rosters, picks, dogs, results, fines, payouts); a dry-run mode previews errors, maps spreadsheet names to coaches, and compares imported totals with the spreadsheet's own standings before committing | P1 |
| A16 | New season wizard | A guided wizard on the admin dashboard that starts a blank season (section 6.2). It imports the NFL schedule (CSV upload in the MVP, automated in Phase 6) and the prior season's division finishes, assigns point values, sets double-dog weeks (Week 2, plus the Thanksgiving and Christmas weeks that the system suggests from the schedule for the admin to confirm), and invites coaches. Archiving the prior season is A22 | P0 |
| A17 | Audit log | Searchable list of every admin action and override with actor, time, before and after values | P0 |
| A18 | Export | CSV export of standings, picks, and the payments ledger | P1 |
| A19 | Simulation mode | Runs a local season: seeded fake coaches and rosters, mock odds and scores adapters, and an admin-controlled fake clock that advances through deadlines, kickoffs, and results; a banner marks simulated data so it can never be mistaken for a real season | P0 |
| A20 | Admin pick visibility | Admins can see every pick at any time, before or after locks. An admin can override rule R21 for one week with a reason (logged), for example to fix a data-entry error | P0 |
| A21 | Job status | Lists each scheduled job with its last run, next due time, and failures, plus score-sync and email delivery state; the admin can retry a failed job, and a failed or missed job raises an alert | P0 |
| A22 | Season archive and rollover | Activating a new season archives the current one after a confirmation: standings freeze, picks become read-only, coaches and settings stay available to carry forward, and nothing is deleted | P1 |

### 6.1 End-of-year payments

The app computes payouts but never moves money. The admin console produces a payout sheet:

1. Pools close in order: the regular-season pool at the end of Week 18, then the dog and playoff pools after the Super Bowl, then the main pool.
2. For each pool the system ranks coaches using the tiebreakers from the rules doc and shows ties it cannot break so the admin decides.
3. The payout table applies the splits: main 60/25/10/5 percent of the $1,800 pot ($1,080, $450, $180, $90); dog $500 and $100; regular season $200 and $100; playoffs $200 and $100.
4. The fine pool is the sum of confirmed fines, split 50/50 between the Going Perf and 0fer record holders.
5. The admin marks each payout paid, with method and date; coaches see their own payout status only.

### 6.2 New season wizard

The Swan starts a season from a guided wizard on the admin dashboard. The dashboard shows a "Start a new season" card whenever no season is in setup, and as the empty state on first run. The wizard creates a blank season in the Setup state; coaches see nothing until the admin opens the roster window in the final step. Progress saves at every step, so the admin can leave and resume.

| Step | What the admin does | What the app checks |
| --- | --- | --- |
| 1. Basics | Chooses to start blank or copy settings from the last season; sets the season year and name; optionally marks it a simulation season (A19) | Only one season in Setup at a time |
| 2. Money and fines | Confirms the buy-in ($100), pool stakes and payout splits, and the fine amount ($5); defaults come from the rules doc | Pool stakes add up to the buy-in |
| 3. Teams and point values | The 32 NFL teams are loaded; the admin enters or uploads last season's division finishes; point values (1 to 4) derive automatically | Each division has exactly one team at each finish |
| 4. Schedule | Uploads the schedule CSV (automated in Phase 6) and previews byes by week | 18 regular-season weeks; each team plays once or has a bye; kickoffs parse as Eastern Time |
| 5. Weeks and dogs | Confirms the weekly deadline (Sunday 1:00 PM ET), the double-dog weeks (Week 2 plus the suggested Thanksgiving and Christmas weeks), playoff multipliers, qualifying points, and the conference bonus | One deadline per week; at least three double-dog weeks |
| 6. League settings | Reviews the toggles for R15, R17, R18, and R19 with their defaults; R1 to R14, R16, R20, and R21 are shown as fixed | None |
| 7. Coaches | Ticks last season's coaches to carry over and pastes email addresses to invite new ones; can add nicknames | Emails are valid and unique |
| 8. Review and create | Sees every choice in one summary with warnings (missing finishes, schedule gaps, no coaches) and creates the season in Setup | Blocking errors must be fixed; warnings can be accepted |

After creation the dashboard shows a checklist. Its main action is "Open roster window," which sends the invites (N1) and lets coaches choose their 9 teams. Week 1 then opens through the normal weekly workflow (section 7).

- W1: Every step can be edited after creation while the season is in Setup, and the draft survives a closed browser.
- W2: Blank means blank. No rosters, picks, or results are copied; only settings and, if chosen, the coach list.
- W3: Creating a season never changes the season in play. Activating the new season archives the current one after a confirmation (A22).
- W4: Every wizard action is written to the audit log, and only admins can use it.
- W5: The wizard works with the seed data and simulation mode, so the first run happens locally with no cloud setup.

## 7. Odds, results and scoring pipeline

Every week moves through five states, and each transition has one owner.

| State | Entered when | Owner | Coach sees |
| --- | --- | --- | --- |
| Draft | The admin creates the week's games from the schedule and enters or pastes lines (MVP); later the odds import does this | System | Nothing |
| Lines approved | The admin approves the spreads and the dog list | Admin | Nothing until published |
| Open | The admin publishes; target is Tuesday | Admin | Picks screen, countdown |
| Locked | Sunday 1:00 PM ET (R6), or an admin lock | System | Read-only picks, live week |
| Final | The admin confirms results | Admin | Scores, recap, updated standings |

**Odds import requirements**

- O1: Lines enter through an adapter interface. The MVP adapter is manual (a lines form plus CSV paste); an automated odds-provider adapter comes in Phase 6 (Milestone 2), so the provider can change without touching business logic.
- O2: In the MVP every line is stored with its entry time, source (manual or CSV), and who entered it. Later automated imports run on a schedule and on a "refresh now" button.
- O3: An imported line never reaches coaches without admin approval. If an imported line changes after approval, the admin sees a "line moved" flag and decides whether to re-approve; approved values stay frozen otherwise.
- O4: The approved spread, not the live line, is what the coach saw at pick time and what the dog points use (R9). Store the approved value on the game.
- O5: A game with a missing or conflicting line is blocked from approval until the admin enters a value manually.

**Results and scoring requirements**

- S1: Final scores import from a scores provider on a schedule starting at each kickoff; the system detects final status and records the winner or a tie. The admin can enter or correct a score manually as a fallback.
- S2: Scoring is a pure function of confirmed results, picks, roster point values, and settings, and recomputes idempotently, so an override or reopened week produces correct totals every time.
- S3: The system computes team points, Perfect Week bonuses, dog points, per-coach weekly totals, and the four pool standings, each stored with a link to the rows that produced it.
- S4: Postponed, suspended, or flexed games are flagged to the admin. A postponed game has no result until it is played; the admin decides whether the week stays open for it.
- S5: A tie scores zero for the team and ends any Perfect Week.
- S6: An admin result override requires a reason and recomputes the week and every standing that depends on it.

* S7: Provider constraints: the API key stays on the server; the app stays inside the free-tier quota with only a handful of calls a week (fetch the week's games, then re-check only games that are not yet final, starting about three hours after kickoff); the provider's terms must allow showing scores to league members; a quota or outage alert goes to the admin, who can fall back to manual entry. Sportradar's trial (30 days, 1,000 calls) is suitable for testing only, since its production pricing is enterprise.

## 8. Communication and notifications

The app sends email and PWA push. Because the commish cannot see missing picks until after the deadline, reminders are the most important notification in the product.

| ID | Event | Audience | Timing | Channels | Coach can mute? |
| --- | --- | --- | --- | --- | --- |
| N1 | Invite | New coach | On invite | Email | No |
| N2 | Lines published, picks open | All coaches | On publish | Email, push | Yes, per channel |
| N3 | Picks reminder 1 | Coaches with incomplete picks | Saturday 6:00 PM ET | Email, push | Yes, per channel (warns that missed picks cost $5) |
| N4 | Picks reminder 2 | Coaches with incomplete picks | Sunday 9:00 AM ET | Email, push | Yes, per channel (warns that missed picks cost $5) |
| N5 | Final warning | Coaches with incomplete picks | Sunday 12:00 PM ET | Push, email | Yes, per channel (warns that missed picks cost $5) |
| N6 | Dog reminder | Coaches with no dog pick | 60 minutes before the earliest open dog kickoff; again before the Monday game | Push, email | Yes, per channel |
| N7 | Pick receipt and edits | The coach | On submit or change | Email | Yes |
| N8 | Admin override | The affected coach | On change | Email, push | No |
| N9 | Week locked, missing-pick list | Admin | At lock | Email, push | No |
| N10 | Results final, weekly recap | All coaches | On confirm | Email, push | Yes, per channel |
| N11 | Line moved after approval | Admin | On change | Push, email | No |
| N12 | Announcement | Chosen audience | On send | Email, push, or both | Yes, per channel |
| N13 | Fine added | The coach | On confirm | Email | No |
| N14 | Payout status | The coach | On mark paid | Email | No |
| N15 | Early-game pick reminder | Coaches who own a team kicking off before the weekly deadline and have not picked it | Once, 60 minutes before that kickoff | Push, email | Yes, per channel |

**Requirements**

- M1: Reminders are recalculated at send time; a coach who has finished their picks never gets a reminder.
- M2: Reminder times and the deadline derive from the league deadline setting, so changing the deadline moves the schedule.
- M3: Push needs an explicit permission prompt after the first successful sign-in; the app works fully without push, and email is always the fallback.
- M4: Each sent notification is logged per coach with channel, time, and delivery status, and the admin can see who was notified before a deadline.
- M5: Announcements support plain text plus links, a preview, scheduling, and an audience filter (all, missing picks this week, or a hand-picked list).
- M6: The in-app feed mirrors every announcement and recap so nothing depends on email delivery.
- M7: Coaches see every message in Eastern Time with the deadline stated in the text, not just a link.
- M8: Optional in v1.1: a league message board where coaches can post comments under announcements. The commish can delete any post. This is P2 and needs moderation rules.

* M9: Every coach has notification settings per category (pick reminders, early-game reminders, dog reminders, announcements, recaps, receipts) and per channel (email, push), plus a master quiet mode that mutes everything non-essential.
* M10: Essential messages cannot be muted: the sign-in link, invites, admin overrides, fines, and payout status.
* M11: The early-game reminder (N15) is sent once per team per week, never repeated.
* M12: When a coach mutes pick reminders or turns on quiet mode, the app shows once that missed picks cost $5.

## 9. Domain model

The domain splits into eight bounded contexts. Picks and Scoring are the core domain: that is where the league's unique rules live and where design effort goes. The others support them or are generic. This is DDD lite: aggregates enforce the rules that must always hold, with no repositories, no mediator, and one DbContext. Contexts connect by ID and by domain events, never by object reference.

### 9.1 Bounded contexts

| Context | Kind | Responsibility | Aggregates |
| --- | --- | --- | --- |
| Picks | Core | What each coach owns and chooses, and when choices lock and become visible | Roster, Ballot |
| Scoring | Core | Turns confirmed results and picks into points, bonuses, standings, and records | WeekScore (standings and records are read models) |
| Schedule | Supporting | NFL teams, weeks, games, lines, dog options, results, and the weekly workflow | Team, SeasonTeam, Week |
| League | Supporting | Seasons, settings, pools, memberships, rulings, and the new-season wizard | Season, Membership |
| Money | Supporting | Buy-ins, fines, pool settlement, and payouts (ledger only) | Ledger, PoolSettlement |
| Communication | Generic | Announcements, notifications, preferences, and push subscriptions | Announcement, Notification, NotificationPreferences, PushSubscription |
| Identity | Generic | Who can sign in and what role they hold | User |
| Platform | Generic | Audit log, scheduled jobs, and history import | AuditEntry, ScheduledJob, ImportBatch |

**How the contexts relate**

| From | To | What flows |
| --- | --- | --- |
| League | Every context | SeasonId, settings, and pool definitions, read-only |
| Identity | League, Picks | UserId, carried on Membership |
| Schedule | Picks, Scoring | Kickoffs, byes, week deadlines, approved spreads, and confirmed results, announced by the events WeekPublished, WeekLocked, and ResultsConfirmed |
| Picks | Scoring | Locked ballots, read by Scoring at scoring time |
| Scoring | Money | Final pool standings, when a pool closes |
| Picks, Schedule, Money | Communication | Events that trigger notifications and receipts |
| History import | League, Picks, Scoring | An anti-corruption layer that turns spreadsheet rows into the same commands the app uses, so imported data obeys the same rules |

### 9.2 Ubiquitous language

These terms mean the same thing in the PRD, the code, and the screens.

| Term | Meaning |
| --- | --- |
| Coach | A user with a Membership in a season |
| Roster | The 9 season teams a coach owns for the season |
| Team pick | One of the up to 3 roster teams a coach backs in a week (also called a start) |
| Dog pick | An underdog chosen from the week's dog options |
| Ballot | A coach's team picks and dog picks for one week |
| Lock time | The moment a pick can no longer change |
| Week state | Draft, Lines approved, Open, Locked, or Final |
| Point value | 1 to 4, from the team's prior-season division finish |
| Perfect Week | All 3 team picks win, for a 3-point bonus; the dog is excluded |
| Going Perf, 0fer | A week where all 3 team picks and the dog win, or all lose |
| Pool | Main, Dog, Regular Season, or Playoff |
| Settlement | Closing a pool and assigning its payouts |

### 9.3 Aggregates and their invariants

Each aggregate has one root. Everything inside it changes in one transaction, and the root's methods are the only way to change it.

| Aggregate | Context | Root and members | Invariants it enforces |
| --- | --- | --- | --- |
| Ballot | Picks | Root: Ballot (membership, week). Members: TeamPick and DogPick entities, each with a LockTime, and a PickChange history | At most 3 distinct team picks, all from the coach's roster; no bye team; no change after a pick's lock time; 1 dog pick (2 in a double-dog week) with distinct teams and games; complete at 3 team picks |
| Roster | Picks | Root: Roster (membership). Members: RosterSlot entities, each pointing to a season team | Exactly 9 distinct season teams once the window closes; changes only while the roster window is open; an admin correction needs a reason |
| WeekScore | Scoring | Root: WeekScore (membership, week). Members: ScoreLine value objects (source, points, reference) | Built only from a confirmed week; recomputing replaces every line; a Perfect Week needs 3 winning team picks; a tie scores zero |
| Week | Schedule | Root: Week (season, number, type, deadline, state). Members: Game entities (kickoff, status, result), line history, one approved line per game, and DogOption entities | State moves forward only, except an admin reopen; cannot publish until every game has an approved line; approved lines are frozen once published; a result needs both scores |
| SeasonTeam | Schedule | Root: SeasonTeam (season, team, prior-season finish, point value) | Each division has exactly one team at each finish; point value derives from the finish |
| Team | Schedule | Root: Team (name, abbreviation, division) | 32 permanent franchises; never edited by a season |
| Season | League | Root: Season (year, name, status, simulation flag). Members: SeasonSettings, PlayoffRules, and RosterWindow value objects, PoolDefinition entities, double-dog week numbers, and Ruling entities (the rulings log, A10) | One active season at a time; pool stakes sum to the buy-in; at least three double-dog weeks; structural settings change only while in Setup |
| Membership | League | Root: Membership (season, user, status). Members: BadgeAward entities (past champion, purped, JTBL, and similar) | One membership per user per season; deactivating keeps all history |
| Ledger | Money | Root: Ledger (membership). Members: LedgerItem entities (buy-in, fine, or payout, with amount, status, paid date, method) | A fine moves Pending to Confirmed or Waived, then Paid; a payout exists only after its pool is settled; amounts are positive |
| PoolSettlement | Money | Root: PoolSettlement (pool). Members: Placing value objects (rank, membership, amount) | Settles once; payouts sum to the pot; ties it cannot break are flagged for the admin |
| Announcement | Communication | Root: Announcement (author, body, audience, channels, schedule) | Sent once; a schedule must be in the future |
| Notification | Communication | Root: Notification (recipient, event key, channel, status, attempts) | At most one per recipient and event key; respects preferences unless essential |
| NotificationPreferences | Communication | Root: preferences per user (category and channel settings, quiet mode) | Essential categories cannot be muted |
| PushSubscription | Communication | Root: one per device (endpoint, keys, label) | Endpoint is unique; expired endpoints are removed |
| User | Identity | Root: User (email, display name, nickname, role, active, show-real-name flag). Members: LoginToken entities | Email is unique; a token is single-use and expires in 15 minutes; only an admin assigns roles |
| ScheduledJob | Platform | Root: ScheduledJob (key, kind, due time, status, lease, attempts, last error) | One claim at a time through the lease; the job key is unique, so work is never queued twice |
| AuditEntry | Platform | Root: AuditEntry (actor, action, entity, before, after, reason, time) | Append-only |
| ImportBatch | Platform | Root: ImportBatch (source file, season, status). Members: ImportRow entities and CoachAlias mappings | A dry run precedes a commit; a committed batch can be rolled back (Milestone 2) |

### 9.4 Value objects and policies

| Name | Kind | Rule |
| --- | --- | --- |
| Money | Value object | Whole cents as an integer; never negative |
| PointValue | Value object | 1 to 4 |
| Spread | Value object | Signed decimal as approved by the admin |
| DogPoints | Value object | The spread rounded up (+6.5 pays 7) |
| Kickoff | Value object | A UTC instant, displayed in Eastern Time |
| LockTime | Value object | The earlier of a team's kickoff and the week deadline; a dog's own kickoff |
| GameResult | Value object | Both scores plus status; winner or tie is derived, never stored separately |
| Email | Value object | Normalized and validated |
| Audience | Value object | All coaches, coaches missing picks this week, or a hand-picked list |
| Strongly typed IDs | Value objects | SeasonId, WeekId, MembershipId, and so on, so the compiler stops a mix-up |
| LockPolicy | Domain service | Computes lock times and answers "can this pick change now?" (R6, R20) |
| PickVisibilityPolicy | Domain service | Answers "can this viewer see that coach's pick?" (R21) |
| ScoringPolicy | Domain service | Team points, Perfect Week, dog points, and playoff points (R3 to R14) |
| TiebreakPolicy | Domain service | Ranks a pool using that pool's tiebreakers from the rules doc |
| PayoutPolicy | Domain service | Splits each pot by the payout percentages and the fine pool by the record holders |

### 9.5 Domain events

Events are raised by aggregates, dispatched in-process after the transaction commits, and handled by idempotent handlers. There is no message broker.

| Event | Raised by | Handled by |
| --- | --- | --- |
| MembershipInvited | Membership | Communication sends the invite (N1) |
| WeekPublished | Week | Communication sends N2; Platform schedules the week's reminder jobs |
| PickSubmitted, PickChanged | Ballot | Communication sends the receipt (N7); Platform writes an audit entry when an admin made the change |
| WeekLocked | Week | Money creates pending fines for incomplete ballots; Communication tells the admin (N9) |
| ResultsConfirmed | Week | Scoring recomputes every WeekScore; Communication sends the recap (N10) |
| FineAssessed | Ledger | Communication sends N13 |
| PoolSettled | PoolSettlement | Ledger creates payout items; Communication sends N14 |
| SeasonActivated | Season | Platform archives the previous season (A22) |

### 9.6 Modeling rules

- Aggregates reference other aggregates by ID only. No navigation properties cross an aggregate or a context boundary.
- One transaction changes one aggregate. Effects on other aggregates go through domain events and their handlers.
- Aggregates stay small: Ballot and WeekScore are per coach per week, and Week holds one week's games.
- Rules live in aggregate methods and the policies in section 9.4, never in handlers or endpoints.
- Scores are derived, never typed: a recompute rebuilds every score line from confirmed results and locked ballots.
- Lines, picks, and settings are versioned; nothing that affects points is updated destructively.
- All kickoff and lock times are stored in UTC and displayed in Eastern Time.
- Playoff rounds are Weeks with their own type and a picks-required flag, on by default because coaches still pick in the playoffs (details in section 12).
- Standings, records, league history, and the live week view are read models: queries that may join across contexts, not aggregates.

### 9.7 How the model maps to the code

- **Swan.Core** has one top-level folder per context (Picks, Scoring, Schedule, League, Money, Communication, Identity, Platform). Each holds its aggregates, value objects, policies, events, and the interfaces it needs.
- **Swan.Data** has one DbContext, one configuration folder per context, and one PostgreSQL schema per context, all in a single migration history.
- **Swan.Services** groups features by context, then slice (for example Picks/SubmitPick). A handler loads one aggregate, calls a method on its root, saves, and lets the events flow.
- **Cross-context reads** go through small query interfaces declared by the consuming context. Scoring, for example, reads locked ballots and confirmed results this way.
- **Architecture tests** fail the build if one context's Core folder references another context's aggregates; only IDs and events may cross.

## 10. Non-functional requirements and tech stack

| Area | Requirement |
| --- | --- |
| Frontend | React with TypeScript, installable PWA with a service worker and web push, mobile-first layouts (most coaches will pick from a phone) |
| Backend | ASP.NET Core Minimal APIs, vertical slices, DDD-lite layering (section 10.1) |
| Data | PostgreSQL via EF Core migrations; scoring, lock, and visibility logic in pure, unit-tested Core code |
| Local orchestration | .NET Aspire AppHost runs Postgres, API, Worker, React, and Mailpit (section 10.3); deployment is agnostic for now |
| Auth | Email magic link, single-use tokens, 15-minute expiry, rate-limited per email and IP; admin role assigned in the database, never self-service |
| Background jobs | A separate Worker host runs reminders, deadline follow-up, score sync, recompute, and delivery (section 10.2); the odds import joins it in Phase 6 |
| Time | UTC storage, Eastern Time display, and a server-side IClock for every lock decision (never the client's) |
| Reliability | Pick locks derive from the server clock at request time, so they never depend on a job. Jobs fire within 60 seconds of their due time, and a missed or failed job raises an admin alert |
| Performance | Picks screen loads in under 2 seconds on a mobile connection; the 1:00 PM Sunday submission spike is expected, with about 30 concurrent users |
| Security | HTTPS outside localhost; hashed tokens; role checks on every endpoint; audit log append-only; no coach can read another coach's picks except as rule R21 allows, enforced on the server |
| Privacy | Stores email and display name only; no payment credentials ever |
| Accessibility | WCAG 2.1 AA for the picks flow; works without push |
| Observability | Structured logs and OpenTelemetry traces, viewed in the Aspire dashboard locally; an admin-visible job and import status page |
| Testing | Unit tests for Core scoring and rules; integration tests per slice against real Postgres (Testcontainers); architecture tests for layer rules; end-to-end tests for the pick and lock flow; a scoring fixture suite from sample weeks |
| Data safety | Seed and reset scripts in the MVP; backups and restore are decided with deployment (section 10.4) |
| Hosting | Deferred; the stack runs from containers so any host can run it (section 10.4) |

### 10.1 Solution structure: vertical slices with DDD-lite layers

| Project | Holds | May reference |
| --- | --- | --- |
| Swan.Core | The domain model organized by bounded context (section 9): aggregates, value objects, and the pure rules: scoring, Perfect Week, per-pick lock times, the visibility rule R21. Also the interfaces for anything external (clock, email, scores, odds, push) | Nothing |
| Swan.Data | EF Core DbContext, one configuration folder and PostgreSQL schema per context, migrations, seed data. No business rules | Core |
| Swan.Services | Use cases as vertical slices, grouped by bounded context, then one folder per feature (for example Picks/SubmitPick or Schedule/ApproveLines), each with its request, validator, handler, and response. Also the adapter implementations (BallDontLie, The Odds API, email, web push) in an Integrations folder | Core, Data |
| Swan.Api | Host with minimal endpoints, authentication, and OpenAPI. One endpoint file per slice that only maps HTTP to a handler call | Services |
| Swan.Worker | Host for scheduled jobs; each job calls a Services handler | Services |
| Swan.AppHost, Swan.ServiceDefaults | Aspire local orchestration and shared telemetry defaults | The hosts |
| Swan.Web | React app that calls the API | None (HTTP only) |
| Tests | Swan.Core.Tests (unit), Swan.Services.Tests (integration against real Postgres), Swan.Architecture.Tests, end-to-end tests for the pick flow | As needed |

**Rules for the structure**

- A slice keeps its request, validation, handler, and response together. Slices never call each other's handlers; shared logic moves into Core.
- Endpoints stay minimal: bind the request, authorize, call the handler, return the result. No logic in endpoints.
- DDD lite means entities carry behavior where an invariant matters (a pick cannot change after its lock; a week cannot score before confirmation), without aggregate ceremony. There are no repositories; handlers use the DbContext directly, and no mediator library is required.
- Handlers return a result type for expected failures (late pick, invalid roster) and reserve exceptions for bugs.
- All time comes from an IClock in Core. The simulation fake clock (A19) is another implementation of it.
- Responses use slice-specific records; domain entities never leave the Services layer.
- Architecture tests fail the build if Core references anything outside itself, Data references Services, or an endpoint references Data.

### 10.2 Background workers

**Decision: a separate Swan.Worker host from the start.** It shares Core, Data, and Services with the API, runs as its own process, and can later be deployed and scaled on its own. Locally the AppHost starts it beside the API.

- **Locks are not timers.** A pick's lock time is derived from kickoff and deadline times and checked against the server clock whenever a pick is written or read. Lock correctness therefore never depends on a job running on time.
- **Jobs handle side effects:** reminders, missed-pick flags and pending fines, score sync, result recompute, and notification delivery.
- **Why separate:** API restarts or extra instances cannot duplicate or drop jobs; the API and worker can scale and fail independently; the worker can be deployed on its own schedule.
- **Safe with more than one instance:** every job is idempotent and claims its work from a persisted job table (job key, due time, status, lease) using row-level claims, so two workers can never send the same reminder twice.
- **Data-driven scheduling:** due times come from the deadline setting and kickoff times, not fixed cron entries, so changing the deadline moves the reminders. A scheduler loop checks the job table every 15 seconds.
- **Library:** start with a plain BackgroundService loop over the job table. Revisit Quartz.NET or Hangfire only if the needs outgrow it.
- **Single-process option:** a configuration flag hosts the worker loop inside the API process for a one-container deploy. It is the same code, off by default.
- **Visibility:** an admin job status page shows each job's last run, next due time, and failures (A21).

| Job | Due | Action |
| --- | --- | --- |
| Pick reminders (N3 to N5) | Saturday 6 PM, Sunday 9 AM, Sunday 12 PM ET, derived from the deadline setting | Notify coaches with incomplete picks who have not muted them |
| Dog reminder (N6) | 60 minutes before the earliest open dog kickoff, and before the Monday game | Notify coaches with no dog pick |
| Early-game reminder (N15) | 60 minutes before the kickoff, once | Notify coaches who own that team and have not picked it |
| Deadline follow-up | The weekly deadline | Flag incomplete weeks, create pending $5 fines, notify the admin (N9) |
| Score sync | About three hours after each kickoff, repeating until final | Fetch scores through the adapter and propose results for admin confirmation |
| Recompute | When the admin confirms a week | Recompute scores and all four standings |
| Notification delivery | Continuous | Send queued email and push, retry failures, write the delivery log |

**Host portability.** Jobs are plain handler classes in Swan.Services; the host only decides when to call them. Because due times live in the job table, every host reduces to one call, "run whatever is due now":

| Host option | How it calls the jobs |
| --- | --- |
| Worker loop (default) | A .NET Worker Service (BackgroundService) checks the job table every 15 seconds |
| Run-once command | `Swan.Worker --once` runs due jobs and exits, so cron, a systemd timer, or a platform scheduled job can run it every minute |
| In-process | The API hosts the same loop behind a configuration flag |
| Azure Functions | A timer trigger every minute calls the same handler. A thin shim, not a rewrite |

There is no universal job format across clouds; Azure Functions timer triggers are specific to that platform. Keeping the jobs as plain handlers behind a "run due jobs" call is what makes moving them cheap.

### 10.3 Local development

- **One command starts everything:** running the AppHost brings up a Postgres container, the API, the Worker, the React dev server, and Mailpit (a local mailbox that catches magic-link and reminder emails).
- **Seed and reset:** commands load the NFL teams, 2025 division finishes, point values, and a demo league with fake coaches and a sample week, and reset the database to that state.
- **Mock adapters by configuration:** fake clock, mock scores, mock odds, and Mailpit email are the defaults. The real BallDontLie adapter switches on with an API key in local secrets.
- **Web push** works on localhost through the service worker; no certificates are needed.
- **No cloud credentials** are required to run, test, or demo the app.
- **CI** builds, runs the unit, integration, and architecture tests, boots the stack in containers, and runs a smoke test of the pick flow.

**Guidance for the coding agents**

- Treat sections 4 to 8 as the source of truth; each ID (R, C, A, O, S, N, M) is a testable requirement.
- Build Core first: scoring, lock times, and the visibility rule, with unit tests, before Data or the API.
- Keep endpoints thin and put logic in handlers and Core. Nothing in Core may reference EF Core, ASP.NET, or HTTP.
- Never compute lock or deadline decisions on the client; always use the server-side IClock.
- Architecture tests must pass in CI before any slice merges.

### 10.4 Deployment (deferred)

Deployment is deliberately undecided; the first goal is a fully working local stack. These notes are carried forward and are not MVP requirements.

| Area | Note |
| --- | --- |
| Hosting | Undecided; Phase 1b decides. Candidates: one VPS running the stack in containers (the likely front-runner under a $10 budget), a VPS with a free hosted database, a PaaS split such as Fly.io plus Neon or DigitalOcean App Platform, or Azure with a hard cost cap. Any host that runs containers works |
| Cost target | Under $10 a month in total, as close to free as possible, whenever deployment happens |
| Email | Resend is the planned sender behind the email interface; it needs a verified subdomain of cernylabs.com (swan.cernylabs.com for the app, mail.cernylabs.com for email) |
| Web push | VAPID keys kept as secrets |
| Infrastructure as code and CI/CD | Chosen with the host; repeatable infrastructure as code remains the goal |
| Worker hosting | Deploy the Worker as its own service, run it from cron or a timer with the run-once command, or use the single-process flag on a one-container host (section 10.2) |
| Database | Hosted PostgreSQL with backups, pooled connections, and tolerance for a wake-up delay after idle |

## 11. Phasing

The work is grouped into three milestones. Milestone 1 is the MVP, enough to run a full simulated season; history import, design, money, and playoffs follow. Each phase ends with exit criteria the agents can test.

| Milestone | Phase | Scope | Exit criteria |
| --- | --- | --- | --- |
| 1 (MVP) | 0. Foundations | Solution structure (Core, Data, Services, Api, Worker, Web), Aspire AppHost running Postgres, API, Worker, React, and Mailpit locally, schema and seed data (NFL teams, 2025 finishes, point values), magic-link auth, roles, audit log, architecture tests, CI that boots the stack | One command starts the whole stack locally; an admin and a coach sign in with a magic link caught in Mailpit; schema migrates and seeds cleanly; architecture tests pass |
| 1 (MVP) | 1. Scoring core | Pure scoring library (R1 to R14), hand-built fixtures for sample weeks, standings queries for all four pools | The fixture suite passes, including ties, byes, collisions, double-dog weeks, and Perfect Week |
| 1 (MVP) | 1b. Hosting research | Compare four hosting splits against the $10 monthly budget: (A) one VPS running the whole stack in containers, (B) a VPS with a free hosted database, (C) a PaaS split such as Fly.io plus Neon or DigitalOcean App Platform, (D) Azure with a hard cost cap. Judge each on cost, backup and restore, worker hosting, HTTPS and custom domain, repeatable deploys (infrastructure as code or a compose file), operations burden, and portability. Prove the front-runner by deploying the Phase 0 stack from scratch | A written recommendation with a cost estimate under $10 a month, a tested from-scratch deploy and restore, and a note on how to move to a different host |
| 1 (MVP) | 2. Admin week workflow, scores import, and simulation | New-season wizard (A16), manual lines entry and CSV paste, lines review and approve, dog list, publish, lock, provider spike (BallDontLie first, The Odds API second), automated final-score import with admin confirm and manual fallback, simulation mode with fake clock and mock score adapter (A1 to A6, A16, A19 to A21, O1 to O5, S1 to S6) | The admin creates a blank season with the wizard and runs a full simulated week end to end, including a partial-pick Thursday case; a real finished game imports and scores correctly |
| 1 (MVP) | 3. Coach picks | Roster view, weekly picks with per-pick locks, dog picks, validation, receipts, visibility rule, live week view, standings, my history (C1 to C11) | A coach completes a simulated week on a phone; locks, collisions, and pick visibility behave per sections 3 and 4 |
| 1 (MVP) | 4. Notifications | Email and push, the reminder schedule, delivery log, announcements (N1 to N15, M1 to M12) | Reminders fire on time in a rehearsal; a coach with complete picks gets none; muted categories send nothing |
| 2 | 5. History import | Master spreadsheet import for all seasons, name mapping, dry run, reconciliation against spreadsheet standings, league history views (A15, C12) | Imported totals for every season match the spreadsheet's standings, or each difference is explained |
| 2 | 6. Automated lines | Odds provider adapter, scheduled line imports saved as drafts for admin approval, line-moved alerts (O1 to O3 automation, N11), automated NFL schedule import; manual entry stays as a fallback | Imported lines match hand-entered lines for a sample week; the admin still approves every line; running cost stays inside the $10 monthly budget |
| 3 | 7. Design pass | Collaborative round on logo, theme, and "goofy" copy, then applied across the app | Approved logo, palette, and tone guide; applied to every screen |
| 3 | 8. Money and records | Fines, payments ledger, payout sheet, records, badges, exports (A11, A12, A14, A17, A18, C14) | A dry-run payout sheet matches a hand calculation |
| 3 | 9. Playoffs and rollover | Playoff scoring and picks, qualification points, conference bonus, season archive and rollover (R12 to R14, A22, C16) | A simulated playoff run matches a hand calculation; the next season starts from the wizard with the prior season archived, without code changes |

**Cutover.** There is no 2026 deadline. The likely path is a local simulated season to flush out bugs, then a pitch to the league for next season. The Google form and spreadsheet remain the league's system of record until you decide otherwise. Playoffs and the payout sheet must be ready before the regular season ends in any season the app runs for real.

## 12. Open questions

Every question is answered and recorded in section 2 and the sections above, so nothing blocks the build.

The provider choice (BallDontLie first, The Odds API second) is confirmed. The first Phase 2 task is the provider spike: if BallDontLie fails any of its three checks (final scores and status, quota, terms), the agent switches to The Odds API and reports why.
