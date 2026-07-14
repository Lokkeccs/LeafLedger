# Authentication glue

MSAL uses the Entra `common` authority and stores its cache in `sessionStorage`. Interactive operations use popup APIs so the main application route remains mounted. Access tokens are acquired here and attached only through the single generated `openapi-fetch` client middleware.

The client does not provision users, memberships, or spaces. Sign-out clears the MSAL active account and the shared TanStack Query cache; server-side identity links remain the source of truth.

Configure the public identifiers in `.env.local` using `.env.example`. A missing client id leaves the shell usable and displays a clear unconfigured state.