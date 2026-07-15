FROM python:3.12-slim@sha256:c3d81d25b3154142b0b42eb1e61300024426268edeb5b5a26dd7ddf64d9daf28

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

WORKDIR /app
RUN groupadd --system voxcrm && useradd --system --gid voxcrm --home-dir /app voxcrm
COPY requirements.lock .
RUN python -m pip install --no-cache-dir --upgrade pip==26.1.2 \
    && python -m pip install --no-cache-dir --require-hashes -r requirements.lock
COPY app ./app
COPY alembic.ini ./
COPY alembic ./alembic

RUN chown -R voxcrm:voxcrm /app
USER voxcrm

EXPOSE 8088
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8088"]
