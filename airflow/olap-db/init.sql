DROP TABLE IF EXISTS users_queue;
DROP TABLE IF EXISTS users_mart;
DROP TABLE IF EXISTS users_history;
DROP VIEW IF EXISTS mv_users_queue;
DROP VIEW IF EXISTS mv_users_history;
CREATE TABLE IF NOT EXISTS users_queue (
    id Int32,
    name String,
    email String,
    age Int32,
    gender String,
    country String,
    address String,
    phone String,
    _op String,
    _ts_ms Int64,
    _source_db String,
    _source_table String,
    _data_source String
) ENGINE = Kafka
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm.public.customers',
    kafka_group_name = 'clickhouse_consumer',
    kafka_format = 'JSONEachRow',
    kafka_num_consumers = 1;

CREATE TABLE IF NOT EXISTS users_mart (
    user_id Int32,
    name String,
    email String,
    age UInt8,
    gender LowCardinality(String),
    country String,
    address String,
    phone String,
    last_update DateTime,
    last_kafka_offset UInt64,
    is_deleted UInt8 DEFAULT 0
) ENGINE = ReplacingMergeTree(last_update)
ORDER BY user_id
PARTITION BY toYYYYMM(last_update);

CREATE TABLE IF NOT EXISTS users_history (
    user_id Int32,
    version UInt64,
    name String,
    email String,
    age UInt8,
    gender String,
    country String,
    address String,
    phone String,
    operation String,
    change_time DateTime,
    kafka_offset UInt64
) ENGINE = MergeTree()
ORDER BY (user_id, change_time)
PARTITION BY toYYYYMM(change_time);

CREATE TABLE IF NOT EXISTS telemetry_data (
    user_id Int32,
    date String,
    prosthesis_type LowCardinality(String),
    muscle_group LowCardinality(String),
    signals_count UInt32,
    signal_frequency_avg Float32,
    signal_duration_avg Float32,
    signal_amplitude_avg Float32,
    signal_duration_total UInt32
) ENGINE = MergeTree()
PARTITION BY left(date, 7)
ORDER BY (user_id, date);

CREATE TABLE IF NOT EXISTS bionicpro_reports (
    user_id Int32,
    crm_name String,
    crm_age UInt8,
    crm_gender LowCardinality(String),
    crm_country String,
    date String,
    prosthesis_type LowCardinality(String),
    muscle_group LowCardinality(String),
    signals_count UInt32,
    signal_frequency_avg Float32,
    signal_duration_avg Float32,
    signal_amplitude_avg Float32,
    signal_duration_total UInt32,
    report_generated_at DateTime DEFAULT now()
) ENGINE = SummingMergeTree()
PARTITION BY left(date, 7)
ORDER BY (user_id, date, prosthesis_type, muscle_group);

CREATE TABLE IF NOT EXISTS kafka_errors (
    error_date DateTime,
    topic String,
    partition Int32,
    offset UInt64,
    error_message String
) ENGINE = MergeTree()
ORDER BY error_date;

CREATE TABLE IF NOT EXISTS init_status (
    script_name String,
    executed_at DateTime,
    status String
) ENGINE = MergeTree()
ORDER BY executed_at;

CREATE MATERIALIZED VIEW IF NOT EXISTS mv_users_queue TO users_mart AS
SELECT
    id AS user_id,
    name,
    email,
    toUInt8(age) AS age,
    gender,
    country,
    address,
    phone,
    now() AS last_update,
    _offset AS last_kafka_offset,
    0 AS is_deleted
FROM users_queue
WHERE _op != 'd';

CREATE MATERIALIZED VIEW IF NOT EXISTS mv_users_history TO users_history AS
SELECT
    id AS user_id,
    _offset AS version,
    name,
    email,
    toUInt8(age) AS age,
    gender,
    country,
    address,
    phone,
    _op AS operation,
    toDateTime(_ts_ms / 1000) AS change_time,
    _offset AS kafka_offset
FROM users_queue;

CREATE MATERIALIZED VIEW IF NOT EXISTS mv_bionicpro_reports TO bionicpro_reports AS
SELECT
    u.user_id,
    u.name AS crm_name,
    u.age AS crm_age,
    u.gender AS crm_gender,
    u.country AS crm_country,
    t.date,
    t.prosthesis_type,
    t.muscle_group,
    t.signals_count,
    t.signal_frequency_avg,
    t.signal_duration_avg,
    t.signal_amplitude_avg,
    t.signal_duration_total,
    now() AS report_generated_at
FROM users_mart AS u
INNER JOIN telemetry_data AS t ON u.user_id = t.user_id
WHERE u.is_deleted = 0;

INSERT INTO init_status VALUES ('full_initialization', now(), 'success');