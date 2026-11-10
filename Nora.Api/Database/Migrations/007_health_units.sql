CREATE TABLE IF NOT EXISTS nora.health_units (
    id uuid PRIMARY KEY,
    name text NOT NULL CHECK (length(name) BETWEEN 2 AND 160),
    region text NOT NULL CHECK (length(region) BETWEEN 2 AND 120),
    address text NOT NULL CHECK (length(address) BETWEEN 2 AND 240),
    latitude double precision NOT NULL CHECK (latitude BETWEEN -85 AND 85),
    longitude double precision NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
