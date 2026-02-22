#

```bash
C:\Users\masny.pavel\work\__Курсы\sprint\09\yandex-arch-pro-09\Task4>curl -X POST http://localhost:8083/connectors -H "Content-Type: application/json" -d @crm-connector.json
{"name":"crm-connector","config":{"connector.class":"io.debezium.connector.postgresql.PostgresConnector","database.hostname":"crm_db","database.port":"5432","database.user":"crm_user","database.password":"crm_password","database.dbname":"crm_db","topic.prefix":"crm","table.include.list":"public.customers","plugin.name":"pgoutput","slot.name":"debezium_slot","publication.name":"debezium_pub","publication.autocreate.mode":"filtered","heartbeat.interval.ms":"5000","transforms":"unwrap","transforms.unwrap.type":"io.debezium.transforms.ExtractNewRecordState","transforms.unwrap.drop.tombstones":"false","transforms.unwrap.delete.handling.mode":"rewrite","key.converter":"org.apache.kafka.connect.json.JsonConverter","value.converter":"org.apache.kafka.connect.json.JsonConverter","key.converter.schemas.enable":"false","value.converter.schemas.enable":"false","name":"crm-connector"},"tasks":[],"type":"source"}
```

```bash
C:\Users\masny.pavel\work\__Курсы\sprint\09\yandex-arch-pro-09\Task4>curl http://localhost:8083/connectors/crm-connector/status
{"name":"crm-connector","connector":{"state":"RUNNING","worker_id":"172.24.0.5:8083"},"tasks":[{"id":0,"state":"RUNNING","worker_id":"172.24.0.5:8083"}],"type":"source"}
```

SQL для тестирования CDC
```sql
INSERT INTO customers (id, name, email, age, gender, country, address, phone) 
VALUES (9993, 'CDC Test4', 'cdc4.test@example.com', 30, 'Male', 'Test Country', 'Test Address', '123-456-7890');
```