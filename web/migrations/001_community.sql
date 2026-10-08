CREATE TABLE users (
  id SERIAL PRIMARY KEY, name VARCHAR(255), email VARCHAR(255) UNIQUE,
  "emailVerified" TIMESTAMPTZ, image TEXT
);
CREATE TABLE accounts (
  id SERIAL PRIMARY KEY, "userId" INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  type VARCHAR(255) NOT NULL, provider VARCHAR(255) NOT NULL, "providerAccountId" VARCHAR(255) NOT NULL,
  refresh_token TEXT, access_token TEXT, expires_at BIGINT, id_token TEXT, scope TEXT, session_state TEXT, token_type TEXT,
  UNIQUE(provider, "providerAccountId")
);
CREATE TABLE sessions (
  id SERIAL PRIMARY KEY, "userId" INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  expires TIMESTAMPTZ NOT NULL, "sessionToken" VARCHAR(255) NOT NULL UNIQUE
);
CREATE INDEX sessions_user ON sessions("userId");
CREATE TABLE verification_token (identifier TEXT NOT NULL, expires TIMESTAMPTZ NOT NULL, token TEXT NOT NULL, PRIMARY KEY(identifier, token));

CREATE TABLE bundles (
  id UUID PRIMARY KEY, owner_id INTEGER NOT NULL REFERENCES users(id),
  title VARCHAR(80) NOT NULL, description VARCHAR(2000) NOT NULL DEFAULT '',
  visibility TEXT NOT NULL CHECK (visibility IN ('public', 'draft', 'deleted')),
  revision INTEGER NOT NULL CHECK (revision > 0), payload JSONB NOT NULL,
  scene_count INTEGER NOT NULL, models JSONB NOT NULL, applications TEXT[] NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(), updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX bundles_public_recent ON bundles(updated_at DESC, id) WHERE visibility = 'public';
CREATE INDEX bundles_owner ON bundles(owner_id, updated_at DESC);
CREATE TABLE bundle_versions (
  bundle_id UUID NOT NULL REFERENCES bundles(id) ON DELETE CASCADE,
  revision INTEGER NOT NULL, title VARCHAR(80) NOT NULL, description VARCHAR(2000) NOT NULL,
  payload JSONB NOT NULL, published BOOLEAN NOT NULL DEFAULT false, created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(bundle_id, revision)
);
-- One row per scene keeps app/model/brand filters on the same scene and monitor.
CREATE TABLE bundle_scenes (
  bundle_id UUID NOT NULL REFERENCES bundles(id) ON DELETE CASCADE,
  ordinal INTEGER NOT NULL, name TEXT NOT NULL, applications TEXT[] NOT NULL, monitors JSONB NOT NULL,
  PRIMARY KEY(bundle_id, ordinal)
);
CREATE INDEX bundle_scenes_apps ON bundle_scenes USING GIN(applications);
