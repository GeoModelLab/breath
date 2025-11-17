library(ncdf4)
library(yaml)
library(abind)

rm(list=ls()) # clean global environment

## set the working direction and the I/O directories ----
setwd(dirname(rstudioapi::getActiveDocumentContext()$path))
getwd()

# Finestra temporale bersaglio
start_target <- as.Date("1980-01-01")
end_target   <- as.Date("2024-12-31")


# NA/code & endian
NA_CODE    <- 255L
endian_out <- "little"

# Tiling (dimensione dei tasselli spaziali per lo streaming)
tile_lon <- 100L
tile_lat <- 100L

# Output
out_root      <- "cells_uint8"   # cartella radice con sottocartelle j_###
manifest_path <- file.path(out_root, "manifest.yml")


# ====== CONFIG ======
nc_paths <- c(
  "fg_ens_mean_0.1deg_reg_v31.0e.nc",
  "hu_ens_mean_0.1deg_reg_v31.0e.nc",
  "rr_ens_mean_0.1deg_reg_v31.0e.nc",
  "tn_ens_mean_0.1deg_reg_v31.0e.nc",
  "tx_ens_mean_0.1deg_reg_v31.0e.nc"  # cambia col 5°
)

# 256 byte per variabile (uint8), 255=NA
N <- 256L
NA_CODE <- 255L
endian_out <- "little"

# Range fisici per quantizzazione 8-bit PER VARIABILE (fissi, niente stime)
range_map <- list(
  tx        = c(-50, 60),  # °C
  tn        = c(-50, 60),  # °C
  rr         = c(0, 500),    # mm/day
  hu = c(0,100),
  fg= c(0,20) #m s-1
)

# dimensione tasselli per streaming
tile_lon <- 100L
tile_lat <- 100L

# Output
out_root      <- "cells_uint8"   # cartella radice con sottocartelle j_###
manifest_path <- file.path(out_root, "manifest.yml")

# -------- HELPERS --------
`%||%` <- function(a,b) if(!is.null(a)) a else b

get_cell_latlon <- function(i, j, lat_arr, lon_arr, nlon, nlat) {
  # entrambi 1D (caso E-OBS tipico)
  if (is.null(dim(lat_arr)) && is.null(dim(lon_arr))) {
    if (length(lat_arr) == nlat && length(lon_arr) == nlon) {
      return(list(lat = lat_arr[j], lon = lon_arr[i]))
    }
  }
  # entrambi 2D
  if (!is.null(dim(lat_arr)) && !is.null(dim(lon_arr))) {
    dlat <- dim(lat_arr); dlon <- dim(lon_arr)
    # [nlon, nlat]
    if (length(dlat)==2 && length(dlon)==2 &&
        dlat[1]==nlon && dlat[2]==nlat &&
        dlon[1]==nlon && dlon[2]==nlat) {
      return(list(lat = lat_arr[i, j], lon = lon_arr[i, j]))
    }
    # [nlat, nlon]
    if (length(dlat)==2 && length(dlon)==2 &&
        dlat[1]==nlat && dlat[2]==nlon &&
        dlon[1]==nlat && dlon[2]==nlon) {
      return(list(lat = lat_arr[j, i], lon = lon_arr[j, i]))
    }
  }
  # lat 2D, lon 1D
  if (!is.null(dim(lat_arr)) && is.null(dim(lon_arr))) {
    dlat <- dim(lat_arr)
    if (length(dlat)==2 && dlat[1]==nlon && dlat[2]==nlat && length(lon_arr)==nlon) {
      return(list(lat = lat_arr[i, j], lon = lon_arr[i]))
    }
    if (length(dlat)==2 && dlat[1]==nlat && dlat[2]==nlon && length(lon_arr)==nlon) {
      return(list(lat = lat_arr[j, i], lon = lon_arr[i]))
    }
  }
  # lat 1D, lon 2D
  if (is.null(dim(lat_arr)) && !is.null(dim(lon_arr))) {
    dlon <- dim(lon_arr)
    if (length(dlon)==2 && dlon[1]==nlon && dlon[2]==nlat && length(lat_arr)==nlat) {
      return(list(lat = lat_arr[j], lon = lon_arr[i, j]))
    }
    if (length(dlon)==2 && dlon[1]==nlat && dlon[2]==nlon && length(lat_arr)==nlat) {
      return(list(lat = lat_arr[j], lon = lon_arr[j, i]))
    }
  }
  stop(sprintf("Formati lat/lon non riconosciuti. lat dim: %s; lon dim: %s",
               if (is.null(dim(lat_arr))) "1D" else paste(dim(lat_arr), collapse="x"),
               if (is.null(dim(lon_arr))) "1D" else paste(dim(lon_arr), collapse="x")))
}


get_time_vals_units <- function(nc) {
  vals  <- tryCatch(nc$dim$time$vals,     error=function(e) NULL)
  units <- tryCatch(nc$dim$time$units,    error=function(e) NULL)
  if (is.null(vals) || length(vals)==0) {
    vals2 <- tryCatch(ncvar_get(nc, "time"), error=function(e) NULL)
    if (!is.null(vals2) && length(vals2)>0) {
      vals <- vals2
      units <- units %||% tryCatch(ncatt_get(nc, "time", "units")$value, error=function(e) NULL)
    }
  }
  if (is.null(vals) || length(vals)==0) {
    ncr <- nc_open(nc$filename, suppress_dimvals = FALSE)
    on.exit(nc_close(ncr), add=TRUE)
    vals  <- ncr$dim$time$vals
    units <- units %||% ncr$dim$time$units
  }
  list(vals=vals, units=units)
}

sanitize_latlon <- function(x) sub(",", ".", sprintf("%.4f", x))
make_fname <- function(lat, lon) sprintf("%s_%s.bin", sanitize_latlon(lat), sanitize_latlon(lon))



nc_time_to_Date <- function(nc){
  tu <- get_time_vals_units(nc)
  vals <- tu$vals; units <- tolower(tu$units)
  m <- regexec("^(seconds|minutes|hours|days) since\\s*([0-9]{4}-[0-9]{2}-[0-9]{2})(?:[ T]([0-9]{2}:[0-9]{2}:[0-9]{2}))?", units)
  mm <- regmatches(units, m)[[1]]
  unit <- mm[2]; origin_date <- mm[3]; origin_time <- ifelse(length(mm)>=4 && !is.na(mm[4]), mm[4], "00:00:00")
  origin <- as.POSIXct(paste(origin_date, origin_time), tz="UTC")
  mult <- switch(unit, seconds=1, minutes=60, hours=3600, days=86400)
  as.Date(origin + vals*mult, tz="UTC")
}
read_block <- function(nc, varname, i0, ni, j0, nj, t_start, ntime, lonname, latname){
  vinfo  <- nc$var[[varname]]
  dnames <- sapply(vinfo$dim, `[[`, "name")
  dlen   <- sapply(vinfo$dim, `[[`, "len"); names(dlen) <- dnames
  start <- setNames(rep(1L, length(dnames)), dnames)
  count <- dlen
  start[lonname] <- i0;  count[lonname] <- ni
  start[latname] <- j0;  count[latname] <- nj
  start["time"]  <- t_start; count["time"] <- ntime
  arr <- ncvar_get(nc, varid=varname, start=start[dnames], count=count[dnames], raw_datavals=TRUE)
  aperm(arr, perm = c(match(lonname,dnames), match(latname,dnames), match("time",dnames)))  # [lon,lat,time]
}
q_params <- function(name, range_map) {
  r <- range_map[[name]]; list(off=r[1], scale=(r[2]-r[1])/254)
}
sanitize_latlon <- function(x) sub(",", ".", sprintf("%.4f", x))  # 4 decimali
make_fname <- function(lat, lon) sprintf("%s_%s.bin", sanitize_latlon(lat), sanitize_latlon(lon))

# -------- MAIN WRITE --------
# apri NetCDF
ncs <- lapply(nc_paths, function(p) nc_open(p, suppress_dimvals = TRUE))
on.exit(lapply(ncs, nc_close), add=TRUE)

# nomi variabili (assumo 1 per file)
vnames <- sapply(ncs, function(nc) names(nc$var)[1])
names(vnames) <- c("fg","rr","hu","tx","tn")  # mappa ai nomi brevi richiesti

# griglia dal primo file
nc0 <- ncs[[1]]; v0 <- vnames[1]
d0n <- sapply(nc0$var[[v0]]$dim, `[[`, "name")
lonname <- intersect(c("lon","longitude"), d0n)[1]
latname <- intersect(c("lat","latitude"),  d0n)[1]
dims0 <- setNames(sapply(nc0$var[[v0]]$dim, `[[`, "len"), d0n)
nlon <- dims0[[lonname]]; nlat <- dims0[[latname]]

# lat/lon per filename
lat_arr <- tryCatch(ncvar_get(nc0, "latitude"), error=function(e) NULL)
if (is.null(lat_arr)) lat_arr <- tryCatch(ncvar_get(nc0, "latitude"), error=function(e) NULL)
lon_arr <- tryCatch(ncvar_get(nc0, "longitude"), error=function(e) NULL)
if (is.null(lon_arr)) lon_arr <- tryCatch(ncvar_get(nc0, "longitude"), error=function(e) NULL)
# Appiattisci in vettori puri (copre vettori, array 1D, mxn<-1, ecc.)
lat_vec <- as.vector(lat_arr)
lon_vec <- as.vector(lon_arr)

# Sanity check (una volta)
stopifnot(length(lat_vec) == nlat, length(lon_vec) == nlon)

# Sanitizer per filename
sanitize_latlon <- function(x) sub(",", ".", sprintf("%.4f", x))
make_fname <- function(lat, lon) sprintf("%s_%s.bin", sanitize_latlon(lat), sanitize_latlon(lon))

# allineamento temporale
dates_list <- lapply(ncs, nc_time_to_Date)
starts <- sapply(dates_list, min); ends <- sapply(dates_list, max)
start_common <- max(start_target, max(starts))
end_common   <- min(end_target,   min(ends))
if (start_common > end_common) stop("Nessuna finestra comune ≥1980 e ≤2024.")

t_start_idx <- sapply(seq_along(ncs), function(k) which(dates_list[[k]] >= start_common)[1])
n_use_each  <- sapply(seq_along(ncs), function(k) sum(dates_list[[k]] >= start_common & dates_list[[k]] <= end_common))
steps       <- min(n_use_each)
t_start_idx <- t_start_idx + (n_use_each - steps)  # allinea a stessi 'steps' finali

# vettore date (per tutti uguale): uint16 = giorni da 1980-01-01
days_from_1980 <- as.integer(seq(from = start_common, length.out = steps, by = "day") - as.Date("1980-01-01"))
if (any(days_from_1980 < 0 | days_from_1980 > 65535)) stop("date fuori dal range uint16 rispetto a 1980-01-01")

# pre-calcola q param per ciascuna variabile
qp <- lapply(names(vnames), function(k) q_params(k, range_map))
names(qp) <- names(vnames)

out_root <- "cells_uint8"  # usa lo stesso nome che compare nel path!
dir.create(out_root, showWarnings = FALSE, recursive = TRUE)

# ================== LOOP TASSELLI (CORRETTO) ==================
written <- 0L
skipped <- 0L

for (j0 in seq(1L, nlat, by = tile_lat)) {
  nj <- min(tile_lat, nlat - j0 + 1L)
  
  for (i0 in seq(1L, nlon, by = tile_lon)) {
    ni <- min(tile_lon, nlon - i0 + 1L)
    
    cat(sprintf("[WRITE] lon %d-%d/%d, lat %d-%d/%d | %s→%s | steps=%d\n",
                i0, i0+ni-1L, nlon, j0, j0+nj-1L, nlat,
                format(start_common), format(end_common), steps))
    
    # ---- 1) LEGGI E QUANTIZZA BLOCCO per ogni variabile ----
    qblocks <- list()  # ognuno: [ni, nj, steps]
    for (key in c("fg","rr","hu","tx","tn")) {
      idx <- match(key, names(vnames))
      
      arr <- read_block(ncs[[idx]], vnames[idx],
                        i0, ni, j0, nj,
                        t_start = t_start_idx[idx],
                        ntime   = steps,
                        lonname = lonname, latname = latname)
      
      # scale/offset/missing dei NetCDF (se presenti)
      sf <- tryCatch(ncatt_get(ncs[[idx]], vnames[idx], "scale_factor")$value, error=function(e) NA)
      of <- tryCatch(ncatt_get(ncs[[idx]], vnames[idx], "add_offset")$value,   error=function(e) NA)
      mv <- tryCatch(ncatt_get(ncs[[idx]], vnames[idx], "_FillValue")$value,   error=function(e) NA)
      if (is.na(mv)) mv <- tryCatch(ncatt_get(ncs[[idx]], vnames[idx], "missing_value")$value, error=function(e) NA)
      if (!is.na(sf) && !is.na(of)) arr <- of + sf * arr
      if (!is.na(mv)) arr[arr == mv] <- NA_real_
      
      sc  <- qp[[key]]$scale
      ofq <- qp[[key]]$off
      q <- round((arr - ofq) / sc)
      q[q < 0]   <- 0L
      q[q > 254] <- 254L
      q[is.na(q)] <- NA_CODE
      storage.mode(q) <- "integer"
      
      qblocks[[key]] <- q  # [ni, nj, steps]
      rm(arr, q); gc(FALSE)
    }
    
    # ---- 2) MASCHERE DI SKIP ----
    # rr: NA veri -> 255
    has_na_rr <- apply(qblocks$rr, c(1,2), function(v) any(v == NA_CODE))
    
    # tx/tn: "mancante" codificato come -50°C -> dopo quantizzazione è codice 0
    has_bad_tx <- apply(qblocks$tx, c(1,2), function(v) any(v == 0L))
    has_bad_tn <- apply(qblocks$tn, c(1,2), function(v) any(v == 0L))
    
    # se vuoi essere ultra prudente, considera anche eventuali 255 (quasi mai presenti su tx/tn):
    # has_bad_tx <- apply(qblocks$tx, c(1,2), function(v) any(v == 0L | v == NA_CODE))
    # has_bad_tn <- apply(qblocks$tn, c(1,2), function(v) any(v == 0L | v == NA_CODE))
    
    # skip se QUALSIASI di queste condizioni è vera
    skip_mask <- has_na_rr | has_bad_tx | has_bad_tn
    
    
    # ---- 3) SCRITTURA FILE PER-CELLA (solo se !skip) ----
    for (jj in 1:nj) {
      for (ii in 1:ni) {
        if (skip_mask[ii, jj]) { 
          skipped <- skipped + 1L
          next 
        }
        
        gi <- i0 + ii - 1L
        gj <- j0 + jj - 1L
        
        # lat/lon per nome (vettoriale 1D)
        plat <- lat_vec[gj]
        plon <- lon_vec[gi]
        
        fpath <- file.path(out_root, make_fname(plat, plon))
        dir.create(dirname(fpath), showWarnings = FALSE, recursive = TRUE)
        con <- file(fpath, "wb")
        
        # HEADER minimal: "PCBN", ver, steps, lat, lon
        writeBin(charToRaw("PCBN"), con)
        writeBin(as.integer(1),     con, size = 1)
        writeBin(as.integer(steps), con, size = 4, endian = endian_out)
        writeBin(as.numeric(plat),  con, size = 8, endian = endian_out)
        writeBin(as.numeric(plon),  con, size = 8, endian = endian_out)
        
        # PAYLOAD: date (uint16), poi fg, rr, hu, tx, tn (uint8)
        writeBin(as.integer(days_from_1980), con, size = 2, endian = endian_out)
        writeBin(as.raw(qblocks$fg[ii, jj, ]), con, size = 1)
        writeBin(as.raw(qblocks$rr[ii, jj, ]), con, size = 1)
        writeBin(as.raw(qblocks$hu[ii, jj, ]), con, size = 1)
        writeBin(as.raw(qblocks$tx[ii, jj, ]), con, size = 1)
        writeBin(as.raw(qblocks$tn[ii, jj, ]), con, size = 1)
        
        close(con)
        written <- written + 1L
      }
    }
    
    rm(qblocks); gc(FALSE)
    cat(sprintf("  -> tile (%d:%d, %d:%d) OK | scritti finora=%d, skippati=%d\n",
                i0, i0+ni-1L, j0, j0+nj-1L, written, skipped))
  }
}
cat(sprintf("Fatto. Scritti=%d, Skippati=%d (NA in rr/tx/tn)\n", written, skipped))




#reading test-----------


read_cell_bin_to_df <- function(path,
                                endian = "little",
                                origin = as.Date("1980-01-01"),
                                ranges = NULL,          # <— NON più q_ranges=q_ranges
                                na_code = 255L) {
  if (is.null(ranges)) ranges <- range_map   # prendi il globale se non passato
  
  con <- file(path, "rb"); on.exit(close(con), add = TRUE)
  
  # Header
  magic <- rawToChar(readBin(con, what = raw(), n = 4))
  if (magic != "PCBN") stop("Formato non riconosciuto (magic != 'PCBN').")
  version <- readBin(con, what = integer(), n = 1, size = 1, signed = FALSE)
  steps   <- readBin(con, what = integer(), n = 1, size = 4, endian = endian)
  lat     <- readBin(con, what = double(),  n = 1, size = 8, endian = endian)
  lon     <- readBin(con, what = double(),  n = 1, size = 8, endian = endian)
  
  # Payload
  days <- readBin(con, what = integer(), n = steps, size = 2, signed = FALSE, endian = endian)
  as_u8 <- function(n) as.integer(readBin(con, what = raw(), n = n, size = 1))
  fg_u8 <- as_u8(steps); rr_u8 <- as_u8(steps); hu_u8 <- as_u8(steps); tx_u8 <- as_u8(steps); tn_u8 <- as_u8(steps)
  
  # Dequant
  deq <- function(q, r, na_code = 255L) {
    sc <- (r[2] - r[1]) / 254
    off <- r[1]
    x <- rep(NA_real_, length(q))
    ok <- (q != na_code)
    x[ok] <- off + sc * q[ok]
    x
  }
  
  df <- data.frame(
    date = origin + days,
    fg   = deq(fg_u8, ranges$fg, na_code),
    rr   = deq(rr_u8, ranges$rr, na_code),
    hu   = deq(hu_u8, ranges$hu, na_code),
    tx   = deq(tx_u8, ranges$tx, na_code),
    tn   = deq(tn_u8, ranges$tn, na_code),
    lat  = lat,
    lon  = lon
  )
  attr(df, "steps") <- steps
  attr(df, "version") <- version
  df
}

# punta al file che hai appena scritto
path <- "cells_uint8/31.6500_-9.3500.bin"  # aggiorna se la tua cartella è diversa
df <- read_cell_bin_to_df(path)

head(df)
#    date         fg    rr   hu    tx    tn   lat    lon
# 1 1980-01-01  ...   ...   ...   ...   ...  25.05 -24.95
# ...

# un controllo veloce
summary(df$tx)
any(is.na(df$rr))  # TRUE se ci sono NA (valori 255)




