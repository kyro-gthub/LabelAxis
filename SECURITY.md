# Security policy

## Supported versions

Security fixes go into the newest release. LabelAxis updates itself (Help › Check for
updates), so please make sure you are on the latest version before reporting.

| Version | Supported |
|---|---|
| 1.0.x (latest) | Yes |
| Older | No |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately instead: on this repository's **Security** tab, choose
**Report a vulnerability**. Only the maintainer sees the report. Please include:

- what an attacker could do, and what they need first (a file you open, access to your network…);
- the steps to show it, with a sample project, data file or request if you can;
- the LabelAxis version (Help › About LabelAxis) and your Windows version.

You can expect a first answer within a week. Once a fix is released, the report can be made
public with credit to you, if you wish.

## What is most worth checking

- **Print requests from other programs**: the watch folder and the web link on the PC
  (`http://localhost:8765/print`). Web pages in a browser are refused, and other computers
  need an access key.
- **Opening files**: `.labelproj` projects (ZIP files), Excel, CSV, Publisher (`.pub`) and
  picture files, which may come from someone else.
- **Database connections**: every read runs read-only, and saved passwords are encrypted for
  the Windows user (DPAPI).
- **Updates**: the update file is read over https only, and each download is checked against
  its SHA-256 fingerprint before it is run.
