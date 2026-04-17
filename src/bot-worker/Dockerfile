# syntax=docker/dockerfile:1.6
# Python 3.12 (newer Pythons like 3.14 don't yet have wheels for every
# ValvePython dep). slim-bookworm keeps the image small; build-essential is
# only needed at install time.

FROM python:3.12-slim-bookworm AS runtime
WORKDIR /app

# Dependencies need a compiler for wheels that don't have manylinux builds yet.
# After pip install finishes, purge build-essential to shrink the final image.
RUN apt-get update && \
    apt-get install -y --no-install-recommends build-essential libffi-dev && \
    rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt && \
    apt-get purge -y --auto-remove build-essential libffi-dev && \
    rm -rf /var/lib/apt/lists/* /root/.cache

COPY src/ src/

# Non-root user.
RUN useradd --uid 1000 --create-home app && chown -R app:app /app
USER app

EXPOSE 4100
ENV PYTHONUNBUFFERED=1

CMD ["python", "-m", "src.main"]
